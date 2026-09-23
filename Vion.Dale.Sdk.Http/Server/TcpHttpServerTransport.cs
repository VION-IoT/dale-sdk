using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <summary>
    ///     The socket a hosted HTTP server answers on: a TCP listener and a deliberately small HTTP/1.1 exchange — one request
    ///     per connection, <c>Content-Length</c> bodies only, capped sizes, a bound on how long a client may take over each
    ///     half of its exchange, and a limit on how many connections are served at once.
    ///     <para>
    ///         It is built on a TCP listener rather than on <see cref="HttpListener" /> because the latter is <c>http.sys</c>
    ///         on Windows, which refuses to bind every interface to a process that is not elevated.
    ///     </para>
    /// </summary>
    internal sealed partial class TcpHttpServerTransport : IHttpServerTransport
    {
        /// <summary>The largest request body accepted; a larger declared length is answered 413.</summary>
        internal const int BodyCap = 1024 * 1024;

        /// <summary>
        ///     How many connections are served at once. Each holds a header buffer from the moment it is served, so a
        ///     connection past the limit is answered 503 and closed before anything of its request is read.
        /// </summary>
        internal const int DefaultConnectionLimit = 64;

        /// <summary>
        ///     The longest head accepted — the request line and headers, up to the blank line that ends them; a longer one is
        ///     answered 431. <see cref="HeadExceedsCap" /> is the one place the boundary is decided.
        /// </summary>
        internal const int HeaderCap = 16 * 1024;

        /// <summary>
        ///     How long a client has, from connecting, to send a complete request — and, once the server has its answer, to
        ///     take the response and close.
        /// </summary>
        internal static readonly TimeSpan DefaultReadBound = TimeSpan.FromSeconds(10);

        private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();

        private readonly Func<TcpListener, Task<TcpClient>> _accept;

        private readonly int _connectionLimit;

        private readonly HashSet<Task> _connections = new();

        // Null unless a development host registered one; see ServeAsync for where a request's exchange closes.
        private readonly IExchangeActivityMonitor? _exchanges;

        private readonly object _gate = new();

        private readonly ILogger<TcpHttpServerTransport> _logger;

        private readonly TimeSpan _readBound;

        private Task? _acceptLoop;

        private bool _disposed;

        private TcpListener? _listener;

        private bool _listening;

        private CancellationTokenSource? _stopping;

        public TcpHttpServerTransport(ILogger<TcpHttpServerTransport> logger, TimeSpan readBound, IExchangeActivityMonitor? exchanges = null) : this(logger,
            readBound,
            DefaultConnectionLimit,
            listener => listener.AcceptTcpClientAsync(),
            exchanges)
        {
        }

        /// <param name="logger">The logger.</param>
        /// <param name="readBound">The bound on each half of a client's exchange.</param>
        /// <param name="connectionLimit">How many connections are served at once.</param>
        /// <param name="accept">Accepts the next connection: the listener's own accept, except where a test makes it fail.</param>
        /// <param name="exchanges">The development host's exchange monitor, when one is registered.</param>
        internal TcpHttpServerTransport(ILogger<TcpHttpServerTransport> logger,
                                        TimeSpan readBound,
                                        int connectionLimit,
                                        Func<TcpListener, Task<TcpClient>> accept,
                                        IExchangeActivityMonitor? exchanges = null)
        {
            _logger = logger;
            _readBound = readBound;
            _connectionLimit = connectionLimit;
            _accept = accept;
            _exchanges = exchanges;
        }

        /// <inheritdoc />
        public bool IsListening
        {
            get
            {
                lock (_gate)
                {
                    return _listening;
                }
            }
        }

        /// <inheritdoc />
        public int ActiveConnections
        {
            get
            {
                lock (_gate)
                {
                    return _connections.Count;
                }
            }
        }

        /// <inheritdoc />
        public void Start(IPAddress listenAddress, int port, IHttpServerExchangeHandler handler)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TcpHttpServerTransport));
            }

            // No address-reuse option is set, and none may be. Bound plainly, the listener rebinds a port whose closed
            // connections still linger in TIME_WAIT — this server's own do, since it closes every connection first — and
            // is refused a port another listener holds. Windows allows the first by default, and .NET sets SO_REUSEADDR on
            // every TCP bind on Linux. ExclusiveAddressUse=false and ReuseAddress=true both add SO_REUSEPORT on Linux, which
            // lets a second listener share a held port and split its connections with the first.
            var listener = new TcpListener(listenAddress, port);
            try
            {
                listener.Start();
            }
            catch
            {
                listener.Stop();

                throw;
            }

            var stopping = new CancellationTokenSource();
            lock (_gate)
            {
                _listener = listener;
                _listening = true;
                _stopping = stopping;
                _acceptLoop = Task.Run(() => AcceptAsync(listener, handler, stopping.Token));
            }

            LogListening(listenAddress, port);
        }

        /// <inheritdoc />
        public void Stop()
        {
            TcpListener? listener;
            CancellationTokenSource? stopping;
            Task? acceptLoop;
            lock (_gate)
            {
                listener = _listener;
                stopping = _stopping;
                acceptLoop = _acceptLoop;
                _listener = null;
                _listening = false;
                _stopping = null;
                _acceptLoop = null;
            }

            if (listener == null)
            {
                return;
            }

            stopping!.Cancel();
            listener.Stop();
            WaitQuietly(acceptLoop!);

            // Only once the accept loop has ended is the set of connections final. The cancellation above closed each one's
            // socket, so what is left is a connection unwinding: a read or a write failing, or a request finishing its wait
            // for the server's gate and then failing to write its response — which the server therefore never records.
            Task[] connections;
            lock (_gate)
            {
                connections = _connections.ToArray();
            }

            foreach (var connection in connections)
            {
                WaitQuietly(connection);
            }

            stopping.Dispose();
            LogStopped();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
        }

        private static void WaitQuietly(Task task)
        {
            try
            {
                task.Wait();
            }
            catch (AggregateException)
            {
                // The task's own failure was already handled where it happened; stopping only needs it finished.
            }
        }

        private async Task AcceptAsync(TcpListener listener, IHttpServerExchangeHandler handler, CancellationToken stopping)
        {
            while (!stopping.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _accept(listener).ConfigureAwait(false);
                }
                catch (Exception) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (SocketException exception)
                {
                    LogAcceptFailed(exception);
                    continue;
                }
                catch (Exception exception)
                {
                    // Nothing else is expected here, and retrying an unknown failure could spin. The listener is closed and
                    // reported as not listening, so a block reading IsListening sees what a client sees: no answer. Disabling
                    // and enabling the server starts a fresh listener.
                    lock (_gate)
                    {
                        _listening = false;
                    }

                    listener.Stop();
                    LogAcceptLoopFailed(exception);

                    return;
                }

                Task connection;
                lock (_gate)
                {
                    if (_connections.Count >= _connectionLimit)
                    {
                        handler.Overloaded();
                        _ = RefuseAsync(client);
                        continue;
                    }

                    connection = ServeAsync(client, handler, stopping);
                    _connections.Add(connection);
                }

                _ = connection.ContinueWith(finished =>
                                            {
                                                lock (_gate)
                                                {
                                                    _connections.Remove(finished);
                                                }
                                            },
                                            TaskScheduler.Default);
            }
        }

        /// <summary>
        ///     Answers a connection past the limit with 503 and closes it, reading nothing of its request: a client that has
        ///     already sent one may see the connection reset rather than the answer.
        /// </summary>
        private async Task RefuseAsync(TcpClient client)
        {
            await Task.Yield();
            using (client)
            {
                using var bound = new CancellationTokenSource(_readBound);
                using var closeOnExpiry = bound.Token.Register(client.Dispose);
                try
                {
                    var bytes = Render(HttpServerResponse.Refusal(HttpStatusCode.ServiceUnavailable), false);
                    await client.GetStream().WriteAsync(bytes, 0, bytes.Length, bound.Token).ConfigureAwait(false);
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
                {
                    // The refused client is gone or too slow to take the answer; nothing more is owed to it.
                }
            }
        }

        /// <summary>
        ///     Answers one connection. The read bound runs from connecting until the request is complete, and again from the
        ///     answer until the client has closed. The wait for the answer itself — which waits for any <c>Sync</c> callback
        ///     running — is the block's time and not the client's, so the bound is not counting then. Disposing the client is
        ///     what ends a read the stream does not cancel on its own, so the bound's expiry and the server stopping both close
        ///     the socket.
        /// </summary>
        private async Task ServeAsync(TcpClient client, IHttpServerExchangeHandler handler, CancellationToken stopping)
        {
            await Task.Yield();
            IDisposable? exchange = null;

            // Whether the server has had its say on this connection: a response written in full, or a refusal. A connection
            // that ends without either — a client gone before its request was complete, the read bound, a stop, a response
            // cut short — is reported abandoned once it has unwound.
            var settled = false;
            using (client)
            {
                using var bound = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                bound.CancelAfter(_readBound);
                using var closeOnExpiry = bound.Token.Register(client.Dispose);
                try
                {
                    var stream = client.GetStream();
                    var (request, refusal) = await ReadRequestAsync(stream, bound.Token).ConfigureAwait(false);
                    if (request == null && refusal == null)
                    {
                        return;
                    }

                    // Opened only once a request has been read in full, because recording it is what a development host waits
                    // for: a refusal written before that records nothing, and a connection that never completes its request
                    // must not hold a stepped settle.
                    if (request != null)
                    {
                        exchange = _exchanges?.OpenExchange($"HTTP server {request.Method} {request.Path} on {client.Client.LocalEndPoint}");
                    }

                    if (refusal != null)
                    {
                        handler.Refused(refusal.StatusCode);
                        settled = true;
                    }

                    var response = refusal;
                    if (response == null)
                    {
                        bound.CancelAfter(Timeout.InfiniteTimeSpan);
                        response = handler.Answer(request!);
                        bound.CancelAfter(_readBound);
                    }

                    var bytes = Render(response, request?.Method == "HEAD");
                    await stream.WriteAsync(bytes, 0, bytes.Length, bound.Token).ConfigureAwait(false);

                    // Recorded only once the whole response is written: a request whose response a hang-up, the bound or a
                    // stop cut short was never answered.
                    if (request != null)
                    {
                        handler.Delivered(request);
                        settled = true;
                    }

                    // The request is recorded or refused; what is left is waiting for the client to close, which a stepped
                    // host must not wait on.
                    exchange?.Dispose();

                    // A refusal can leave request bytes unread, and closing a socket with unread input resets the connection,
                    // which discards the response the client has not read yet. Half-closing and draining until the client
                    // closes delivers the response first.
                    client.Client.Shutdown(SocketShutdown.Send);
                    var discard = new byte[4096];
                    while (await stream.ReadAsync(discard, 0, discard.Length, bound.Token).ConfigureAwait(false) > 0)
                    {
                    }
                }
                catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
                {
                    // The client hung up, the read bound elapsed, or the server is stopping. There is nobody left to answer,
                    // and other connections are unaffected.
                }
                catch (Exception exception)
                {
                    LogConnectionFailed(exception);
                }
                finally
                {
                    exchange?.Dispose();
                    if (!settled)
                    {
                        handler.Abandoned();
                    }
                }
            }
        }

        private static async Task<(HttpServerExchange? Exchange, HttpServerResponse? Refusal)> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[HeaderCap + 4096];
            var filled = 0;
            int headerEnd;
            while ((headerEnd = IndexOf(buffer, filled, HeaderTerminator)) < 0)
            {
                // Not found in what has arrived, the blank line can still start in its last three bytes, so the head is at
                // least that long. Judged on that length rather than on how much has arrived, a head of exactly the cap is
                // served however the network splits it.
                if (HeadExceedsCap(filled - (HeaderTerminator.Length - 1)))
                {
                    return (null, HttpServerResponse.Refusal((HttpStatusCode)431));
                }

                var read = await stream.ReadAsync(buffer, filled, buffer.Length - filled, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return (null, null);
                }

                filled += read;
            }

            if (HeadExceedsCap(headerEnd))
            {
                return (null, HttpServerResponse.Refusal((HttpStatusCode)431));
            }

            var head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
            if (!TryParseHead(head, out var method, out var path, out var query, out var headers))
            {
                return (null, HttpServerResponse.Refusal(HttpStatusCode.BadRequest));
            }

            if (headers.ContainsKey("Transfer-Encoding"))
            {
                return (null, HttpServerResponse.Refusal(HttpStatusCode.LengthRequired));
            }

            // No Content-Length and no transfer encoding means no body: whatever follows the head is not read as one.
            var contentLength = 0L;
            if (headers.TryGetValue("Content-Length", out var declaredLength) && !long.TryParse(declaredLength, NumberStyles.None, CultureInfo.InvariantCulture, out contentLength))
            {
                // A length of digits alone that does not fit a long is a declared body over the cap, not a malformed one.
                if (declaredLength.Length == 0 || !declaredLength.All(character => character is >= '0' and <= '9'))
                {
                    return (null, HttpServerResponse.Refusal(HttpStatusCode.BadRequest));
                }

                contentLength = long.MaxValue;
            }

            if (contentLength > BodyCap)
            {
                return (null, HttpServerResponse.Refusal(HttpStatusCode.RequestEntityTooLarge));
            }

            var body = new byte[contentLength];
            var bodyStart = headerEnd + HeaderTerminator.Length;
            var buffered = Math.Min(filled - bodyStart, body.Length);
            Array.Copy(buffer, bodyStart, body, 0, buffered);
            while (buffered < body.Length)
            {
                var read = await stream.ReadAsync(body, buffered, body.Length - buffered, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return (null, null);
                }

                buffered += read;
            }

            return (new HttpServerExchange(method, path, query, headers, body), null);
        }

        /// <summary>
        ///     Parses the request line and the headers. Anything this server does not understand is a malformed request rather
        ///     than something to guess at: a version other than HTTP/1.0 or 1.1, a target that is neither a path nor an absolute
        ///     URL, a header with no name, a folded header line, or two different lengths.
        /// </summary>
        private static bool TryParseHead(string head, out string method, out string path, out string query, out Dictionary<string, string> headers)
        {
            method = path = query = string.Empty;
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length != 3 || !IsToken(requestLine[0]) || requestLine[2] is not ("HTTP/1.1" or "HTTP/1.0"))
            {
                return false;
            }

            method = requestLine[0];
            var target = requestLine[1];
            if (!target.StartsWith("/", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate(target, UriKind.Absolute, out var absolute) || (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps))
                {
                    return false;
                }

                target = absolute.PathAndQuery;
            }

            var queryStart = target.IndexOf('?');
            path = queryStart < 0 ? target : target.Substring(0, queryStart);
            query = queryStart < 0 ? string.Empty : target.Substring(queryStart + 1);

            for (var index = 1; index < lines.Length; index++)
            {
                var line = lines[index];
                var colon = line.IndexOf(':');
                if (colon <= 0 || !IsToken(line.Substring(0, colon)))
                {
                    return false;
                }

                var name = line.Substring(0, colon);
                var value = line.Substring(colon + 1).Trim();
                if (headers.TryGetValue(name, out var existing))
                {
                    if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) && existing != value)
                    {
                        return false;
                    }

                    headers[name] = string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) ? value : existing + ", " + value;
                }
                else
                {
                    headers[name] = value;
                }
            }

            return true;
        }

        private static bool IsToken(string value)
        {
            return value.Length > 0 && value.All(character => character > 32 && character < 127 && "()<>@,;:\\\"/[]?={}".IndexOf(character) < 0);
        }

        private static bool HeadExceedsCap(int headLength)
        {
            return headLength > HeaderCap;
        }

        /// <summary>
        ///     Renders the status line, the headers and the body. A 204 or 304 carries no body and no length; a response to
        ///     <c>HEAD</c> carries the length its body has and not the body itself.
        /// </summary>
        private static byte[] Render(HttpServerResponse response, bool answersHead)
        {
            var status = (int)response.StatusCode;
            var carriesBody = status != 204 && status != 304;
            var head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(ReasonPhrase(status)).Append("\r\n");
            if (carriesBody)
            {
                if (response.ContentType != null)
                {
                    head.Append("Content-Type: ").Append(response.ContentType).Append("\r\n");
                }

                head.Append("Content-Length: ").Append(response.Body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }

            foreach (var header in response.Headers)
            {
                head.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }

            head.Append("Connection: close\r\n\r\n");
            var headBytes = Encoding.ASCII.GetBytes(head.ToString());

            if (!carriesBody || answersHead)
            {
                return headBytes;
            }

            var bytes = new byte[headBytes.Length + response.Body.Length];
            headBytes.CopyTo(bytes, 0);
            response.Body.Span.CopyTo(bytes.AsSpan(headBytes.Length));

            return bytes;
        }

        private static string ReasonPhrase(int status)
        {
            return status switch
            {
                200 => "OK",
                201 => "Created",
                202 => "Accepted",
                204 => "No Content",
                301 => "Moved Permanently",
                302 => "Found",
                304 => "Not Modified",
                400 => "Bad Request",
                401 => "Unauthorized",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                409 => "Conflict",
                411 => "Length Required",
                413 => "Content Too Large",
                431 => "Request Header Fields Too Large",
                500 => "Internal Server Error",
                503 => "Service Unavailable",
                _ => string.Empty,
            };
        }

        private static int IndexOf(byte[] buffer, int length, byte[] pattern)
        {
            return buffer.AsSpan(0, length).IndexOf(pattern);
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "HTTP server listening on {ListenAddress}:{Port}")]
        partial void LogListening(IPAddress listenAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "HTTP server stopped")]
        partial void LogStopped();

        [LoggerMessage(Level = LogLevel.Warning, Message = "HTTP server could not accept a connection")]
        partial void LogAcceptFailed(Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "HTTP server stopped accepting connections after an unexpected failure; disable and enable it to listen again")]
        partial void LogAcceptLoopFailed(Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "HTTP server failed while answering a connection")]
        partial void LogConnectionFailed(Exception exception);
    }
}