using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <inheritdoc />
    internal sealed partial class LogicBlockHttpServer : ILogicBlockHttpServer, IHttpServerExchangeHandler
    {
        /// <summary>
        ///     How many body bytes the answered requests the server keeps may hold between them. The count alone would let a
        ///     block that never takes its requests keep <see cref="ReceivedRequestCapacity" /> full-sized bodies.
        /// </summary>
        internal const int ReceivedRequestBodyBudget = 4 * 1024 * 1024;

        /// <summary>
        ///     How many answered requests the server keeps for a block that has not taken them. A block that serves and never
        ///     takes would otherwise grow the log for as long as clients keep asking.
        /// </summary>
        internal const int ReceivedRequestCapacity = 256;

        private const int MaxPort = 65535;

        // Port 0 binds an ephemeral port the block is never told, so no client can be pointed at it. It is also what an
        // unset configuration field holds.
        private const int MinPort = 1;

        /// <summary>
        ///     Guards the route table and the request log against the transport's threads; every <c>Sync</c> callback runs
        ///     holding it, which is what makes a republish atomic against the requests being answered.
        /// </summary>
        private readonly object _gate = new();

        private readonly ILogger<LogicBlockHttpServer> _logger;

        private readonly Queue<HttpServerRequest> _received = new();

        private readonly Dictionary<string, Dictionary<string, HttpServerResponse>> _routes = new(StringComparer.Ordinal);

        private readonly TimeProvider _timeProvider;

        private readonly IHttpServerTransport _transport;

        private bool _disposed;

        private int _droppedRequestCount;

        private bool _isEnabled;

        // UTC ticks of the most recent arrival; 0 = none. A long read with Volatile semantics because the writers are
        // transport threads and the reader is the block's actor, and a multi-word struct copy could tear.
        private long _lastRequestAtUtcTicks;

        private IPAddress _parsedListenAddress = IPAddress.Loopback;

        private long _receivedBodyBytes;

        // A depth, not a flag: the gate is re-entrant, so a nested Sync returning would clear a flag while the outer
        // callback still holds the gate — and the guard exists for exactly that callback.
        private int _syncCallbackDepth;

        public LogicBlockHttpServer(IHttpServerTransport transport, TimeProvider timeProvider, ILogger<LogicBlockHttpServer> logger)
        {
            _transport = transport;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <summary>
        ///     Looks a request up in what the block last published, on a transport thread, stamping its arrival. The gate is what
        ///     a running <c>Sync</c> callback holds, so a request arriving mid-republish waits for the table the callback leaves
        ///     behind. Nothing is recorded yet: the response may never reach the client.
        /// </summary>
        HttpServerResponse IHttpServerExchangeHandler.Answer(HttpServerExchange exchange)
        {
            lock (_gate)
            {
                exchange.ReceivedAt = _timeProvider.GetUtcNow();
                if (!_routes.TryGetValue(exchange.Path, out var byMethod))
                {
                    return HttpServerResponse.NotFound();
                }

                return byMethod.TryGetValue(exchange.Method, out var response) ? response :
                           HttpServerResponse.MethodNotAllowed(byMethod.Keys.OrderBy(method => method, StringComparer.Ordinal));
            }
        }

        /// <summary>
        ///     Records a request whose response has been written, dropping the oldest requests kept until both the count and the
        ///     body-byte budget hold; a request whose body alone is over the budget is dropped itself.
        /// </summary>
        void IHttpServerExchangeHandler.Delivered(HttpServerExchange exchange)
        {
            lock (_gate)
            {
                // Concurrent connections finish in any order, so the most recent arrival is not always the last one recorded.
                if (exchange.ReceivedAt.UtcTicks > Volatile.Read(ref _lastRequestAtUtcTicks))
                {
                    Volatile.Write(ref _lastRequestAtUtcTicks, exchange.ReceivedAt.UtcTicks);
                }

                if (exchange.Body.Length > ReceivedRequestBodyBudget)
                {
                    _droppedRequestCount++;

                    return;
                }

                while (_received.Count == ReceivedRequestCapacity || _receivedBodyBytes + exchange.Body.Length > ReceivedRequestBodyBudget)
                {
                    _receivedBodyBytes -= _received.Dequeue().Body.Length;
                    _droppedRequestCount++;
                }

                _received.Enqueue(new HttpServerRequest(exchange.Method,
                                                        exchange.Path,
                                                        exchange.Query,
                                                        exchange.Headers,
                                                        exchange.Body,
                                                        exchange.ReceivedAt));
                _receivedBodyBytes += exchange.Body.Length;
            }
        }

        /// <inheritdoc />
        public bool IsEnabled
        {
            get => _isEnabled;

            set
            {
                EnsureNotInSyncCallback(nameof(IsEnabled));
                if (_isEnabled == value)
                {
                    return;
                }

                if (value)
                {
                    // Owned here rather than by each transport, so the kit's in-memory transport refuses exactly as the socket does.
                    if (_disposed)
                    {
                        throw new ObjectDisposedException(nameof(ILogicBlockHttpServer), "A disposed HTTP server cannot be enabled again; create a new one from the factory.");
                    }

                    _transport.Start(_parsedListenAddress, Port, this);
                    LogEnabled(ListenAddress!, Port);
                }
                else
                {
                    _transport.Stop();
                    LogDisabled();
                }

                _isEnabled = value;
            }
        }

        /// <inheritdoc />
        public string? ListenAddress
        {
            get;

            set
            {
                EnsureDisabled(nameof(ListenAddress));
                if (string.IsNullOrWhiteSpace(value) || !IPAddress.TryParse(value, out var parsed))
                {
                    throw new FormatException($"'{value}' is not a valid IP address.");
                }

                _parsedListenAddress = parsed;
                field = value;
            }
        } = "127.0.0.1";

        /// <inheritdoc />
        public int Port
        {
            get;

            set
            {
                EnsureDisabled(nameof(Port));
                if (value is < MinPort or > MaxPort)
                {
                    throw new FormatException(string.Format(CultureInfo.InvariantCulture, "Port {0} is outside the valid range ({1}-{2}).", value, MinPort, MaxPort));
                }

                field = value;
            }
        } = 8080;

        /// <inheritdoc />
        public bool IsListening
        {
            get => _transport.IsListening;
        }

        /// <inheritdoc />
        public DateTimeOffset? LastRequestAt
        {
            get
            {
                var utcTicks = Volatile.Read(ref _lastRequestAtUtcTicks);

                return utcTicks == 0 ? null : new DateTimeOffset(utcTicks, TimeSpan.Zero);
            }
        }

        /// <inheritdoc />
        public void Sync(Action<IHttpServerSnapshot> access)
        {
            lock (_gate)
            {
                _syncCallbackDepth++;
                var snapshot = new Snapshot(this);
                try
                {
                    access(snapshot);
                }
                finally
                {
                    snapshot.End();
                    _syncCallbackDepth--;
                }
            }
        }

        /// <inheritdoc />
        public T Sync<T>(Func<IHttpServerSnapshot, T> access)
        {
            lock (_gate)
            {
                _syncCallbackDepth++;
                var snapshot = new Snapshot(this);
                try
                {
                    return access(snapshot);
                }
                finally
                {
                    snapshot.End();
                    _syncCallbackDepth--;
                }
            }
        }

        public void Dispose()
        {
            EnsureNotInSyncCallback(nameof(Dispose));

            // Set directly rather than through the setter: disposal is not a disable, and the transport stops itself.
            _isEnabled = false;
            _disposed = true;
            _transport.Dispose();
        }

        private void EnsureDisabled(string propertyName)
        {
            if (IsEnabled)
            {
                throw new
                    InvalidOperationException($"{propertyName} can only be changed while the server is disabled. Disable the server, update the configuration, then re-enable it.");
            }
        }

        private void EnsureNotInSyncCallback(string memberName)
        {
            // Stopping the transport waits for the requests it is answering, and a request being answered waits for the
            // gate the Sync callback holds — calling this from inside the callback would deadlock the actor thread for good.
            if (_syncCallbackDepth > 0)
            {
                throw new
                    InvalidOperationException($"{memberName} must not be called from inside a Sync callback — requests being answered wait for that callback to return. Act on the requests after the callback returns.");
            }
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "HTTP server enabled on {ListenAddress}:{Port}")]
        partial void LogEnabled(string listenAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "HTTP server disabled")]
        partial void LogDisabled();

        /// <summary>
        ///     What one <c>Sync</c> callback sees. Every member runs holding the gate the callback holds, and refuses once the
        ///     callback has returned, so a snapshot kept past its callback cannot change the table without the gate.
        /// </summary>
        private sealed class Snapshot : IHttpServerSnapshot
        {
            private readonly LogicBlockHttpServer _server;

            private bool _ended;

            public Snapshot(LogicBlockHttpServer server)
            {
                _server = server;
            }

            public int DroppedRequestCount
            {
                get
                {
                    EnsureLive();

                    return _server._droppedRequestCount;
                }
            }

            public void SetResponse(HttpMethod method, string path, HttpServerResponse response)
            {
                EnsureLive();
                RefuseUnusableRoute(method, path);
                if (response == null)
                {
                    throw new ArgumentNullException(nameof(response));
                }

                if (!_server._routes.TryGetValue(path, out var byMethod))
                {
                    byMethod = new Dictionary<string, HttpServerResponse>(StringComparer.Ordinal);
                    _server._routes[path] = byMethod;
                }

                byMethod[method.Method] = response;
            }

            public bool RemoveResponse(HttpMethod method, string path)
            {
                EnsureLive();
                RefuseUnusableRoute(method, path);
                if (!_server._routes.TryGetValue(path, out var byMethod) || !byMethod.Remove(method.Method))
                {
                    return false;
                }

                // A path left with no method must answer 404, not a 405 whose Allow names nothing.
                if (byMethod.Count == 0)
                {
                    _server._routes.Remove(path);
                }

                return true;
            }

            public void ClearResponses()
            {
                EnsureLive();
                _server._routes.Clear();
            }

            public IReadOnlyList<HttpServerRequest> TakeReceivedRequests()
            {
                EnsureLive();
                var taken = _server._received.ToArray();
                _server._received.Clear();
                _server._receivedBodyBytes = 0;
                _server._droppedRequestCount = 0;

                return taken;
            }

            public void End()
            {
                _ended = true;
            }

            private static void RefuseUnusableRoute(HttpMethod method, string path)
            {
                if (method == null)
                {
                    throw new ArgumentNullException(nameof(method));
                }

                if (string.IsNullOrEmpty(path))
                {
                    throw new ArgumentException("A route's path is not empty; the root path is \"/\".", nameof(path));
                }

                if (path[0] != '/' || path.IndexOf('?') >= 0)
                {
                    throw new ArgumentException($"'{path}' is not a route path: a path starts with '/' and carries no query, which routes nothing.", nameof(path));
                }
            }

            private void EnsureLive()
            {
                if (_ended)
                {
                    throw new InvalidOperationException("This server snapshot belongs to a Sync callback that has already returned. Take a fresh snapshot inside a new Sync call.");
                }
            }
        }
    }
}