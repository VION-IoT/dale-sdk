using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Http.TestKit
{
    /// <summary>
    ///     The requests one harness has recorded and the ones still waiting for the test's answer, oldest first.
    /// </summary>
    internal sealed class HeldExchanges
    {
        /// <summary>
        ///     The call a block is making right now, visible to the handler the call reaches. The executor starts the exchange
        ///     inside that call, so the handler sees the call's timeout and can find the call's task once it has returned.
        /// </summary>
        private static readonly AsyncLocal<ExchangeCall?> CurrentCall = new();

        /// <summary>
        ///     The exchanges expired by the clock callback running on this thread. Thread-static rather than async-local: a
        ///     token callback runs in the execution context captured when it was registered, not in the clock's, so only the
        ///     thread identifies the callback that cancelled it.
        /// </summary>
        [ThreadStatic]
        private static List<HeldExchange>? _expiredByClockCallback;

        private readonly object _gate = new();

        private readonly LinkedList<HeldExchange> _outstanding = new();

        private readonly List<FakeHttpRequest> _requests = new();

        public IReadOnlyList<FakeHttpRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToArray();
                }
            }
        }

        public int PendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _outstanding.Count;
                }
            }
        }

        /// <summary>
        ///     Runs one executor call with its timeout visible to the handler, and keeps the task the call returns so an answer
        ///     can wait for the exchange it releases.
        /// </summary>
        public Task Track(TimeSpan? timeout, Func<Task> execute)
        {
            var call = new ExchangeCall(timeout);
            var previous = CurrentCall.Value;
            CurrentCall.Value = call;
            try
            {
                var task = execute();
                call.Task = task;

                return task;
            }
            finally
            {
                CurrentCall.Value = previous;
            }
        }

        /// <summary>
        ///     Records a request reaching the innermost handler and holds it until the test answers it, or until the per-request
        ///     timeout cancels it.
        /// </summary>
        public Task<HttpResponseMessage> Hold(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = CurrentCall.Value;
            var recorded = Record(request, call?.Timeout);
            var exchange = new HeldExchange(call);
            LinkedListNode<HeldExchange> node;
            lock (_gate)
            {
                _requests.Add(recorded);
                node = _outstanding.AddLast(exchange);
            }

            // Registered outside the gate: a token already cancelled — a timeout of zero — runs this at once, on this thread.
            cancellationToken.Register(() =>
                                       {
                                           lock (_gate)
                                           {
                                               if (node.List != null)
                                               {
                                                   _outstanding.Remove(node);
                                               }
                                           }

                                           exchange.Completion.TrySetCanceled(cancellationToken);
                                           _expiredByClockCallback?.Add(exchange);
                                       });

            return exchange.Completion.Task;
        }

        /// <summary>
        ///     Runs one callback of the harness's clock, then waits for every exchange it expired, as an answer waits for the
        ///     exchange it released. The wait comes only after the callback has returned, when no token callback is still
        ///     running: waiting inside the token callback hangs on a context that sends the exchange to the thread pool, because
        ///     what is left of the exchange there still has to release its hold on that token.
        /// </summary>
        public void RunClockCallback(TimerCallback callback, object? state)
        {
            var previous = _expiredByClockCallback;
            var expired = new List<HeldExchange>();
            _expiredByClockCallback = expired;
            try
            {
                callback(state);
            }
            finally
            {
                _expiredByClockCallback = previous;
            }

            foreach (var exchange in expired)
            {
                exchange.Settle();
            }
        }

        /// <summary>
        ///     Takes the oldest outstanding request out of the queue, or refuses when there is none, naming the member asked.
        /// </summary>
        public HeldExchange TakeOldest(string member)
        {
            lock (_gate)
            {
                var oldest = _outstanding.First ??
                             throw new
                                 InvalidOperationException($"{member} found no outstanding HTTP request: the block issued none, or every one it issued has already been answered or has timed out.");
                _outstanding.RemoveFirst();

                return oldest.Value;
            }
        }

        private static FakeHttpRequest Record(HttpRequestMessage request, TimeSpan? timeout)
        {
            // The non-validated view renders each header as the platform writes it to the wire, with the separator its
            // grammar uses — a space between User-Agent products, a comma between list values.
            var headers = request.Headers
                                 .NonValidated
                                 .Concat(request.Content?.Headers.NonValidated ?? Enumerable.Empty<KeyValuePair<string, HeaderStringValues>>())
                                 .ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            // The content is read on the calling thread: the SDK serializes a body into memory before the exchange starts, and
            // a SendRequest caller's content is read the same way the platform would read it to send.
            var body = request.Content == null ? null : Encoding.UTF8.GetString(request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());

            return new FakeHttpRequest(request.Method,
                                       request.RequestUri!,
                                       headers,
                                       body,
                                       request.Content?.Headers.ContentType?.ToString(),
                                       timeout);
        }

        /// <summary>
        ///     One executor call a block made: the timeout it passed and, once the call has returned, the exchange's task.
        /// </summary>
        internal sealed class ExchangeCall
        {
            public TimeSpan? Timeout { get; }

            public Task? Task { get; set; }

            public ExchangeCall(TimeSpan? timeout)
            {
                Timeout = timeout;
            }
        }

        /// <summary>
        ///     A request held for the test's answer.
        /// </summary>
        internal sealed class HeldExchange
        {
            private readonly ExchangeCall? _call;

            /// <summary>
            ///     Completed with the test's answer. Default options on purpose: completing it runs the SDK's exchange on the
            ///     test's thread, up to the point it hands the callback to the block's dispatcher.
            /// </summary>
            public TaskCompletionSource<HttpResponseMessage> Completion { get; } = new();

            public HeldExchange(ExchangeCall? call)
            {
                _call = call;
            }

            /// <summary>
            ///     Waits for the exchange this answer or expiry released. The release completes the held response inline, and
            ///     every await between the handler and the block's dispatcher declines the caller's synchronization context, so
            ///     the exchange has normally finished before this is reached, and whatever remains runs on the thread pool.
            ///     Nothing of it can be waiting for the thread this call blocks, even where that thread owns the test's context;
            ///     an await in the SDK's exchange that captured the context would break that, and hang here without a diagnostic.
            /// </summary>
            public void Settle()
            {
                if (_call?.Task is not { } task)
                {
                    return;
                }

                try
                {
                    task.Wait();
                }
                catch (AggregateException)
                {
                    // The exchange reports its own failures to the block's error callback; this only waits for it to end.
                }
            }
        }
    }

    /// <summary>
    ///     The innermost message handler of the harness's client: every request the SDK composes stops here and is held.
    /// </summary>
    internal sealed class HoldingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HeldExchanges _exchanges;

        public HoldingHttpMessageHandler(HeldExchanges exchanges)
        {
            _exchanges = exchanges;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _exchanges.Hold(request, cancellationToken);
        }
    }

    /// <summary>
    ///     The harness's clock as the SDK sees it: every reading is the caller's clock, and every timer runs its callback
    ///     through the held exchanges, so an advance that expires a request returns only once the exchange has handed the
    ///     timeout to the block's dispatcher.
    /// </summary>
    internal sealed class SettlingTimeProvider : TimeProvider
    {
        private readonly HeldExchanges _exchanges;

        private readonly TimeProvider _inner;

        public override TimeZoneInfo LocalTimeZone
        {
            get => _inner.LocalTimeZone;
        }

        public override long TimestampFrequency
        {
            get => _inner.TimestampFrequency;
        }

        public SettlingTimeProvider(TimeProvider inner, HeldExchanges exchanges)
        {
            _inner = inner;
            _exchanges = exchanges;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _inner.GetUtcNow();
        }

        public override long GetTimestamp()
        {
            return _inner.GetTimestamp();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            return _inner.CreateTimer(timerState => _exchanges.RunClockCallback(callback, timerState), state, dueTime, period);
        }
    }

    /// <summary>
    ///     Forwards every call to the real executor unchanged, keeping the task each call returns — the task of the block's
    ///     own call, which the block itself never receives.
    /// </summary>
    internal sealed class TrackingHttpRequestExecutor : IHttpRequestExecutor
    {
        private readonly HeldExchanges _exchanges;

        private readonly IHttpRequestExecutor _inner;

        public TrackingHttpRequestExecutor(IHttpRequestExecutor inner, HeldExchanges exchanges)
        {
            _inner = inner;
            _exchanges = exchanges;
        }

        public Task ExecuteRequestAsync<TContent>(IActorDispatcher dispatcher,
                                                  string url,
                                                  HttpMethod httpMethod,
                                                  Func<HttpResponseMessage, Task<TContent>> getResponseContent,
                                                  Action<TContent> successCallback,
                                                  Action<Exception>? errorCallback = null,
                                                  Dictionary<string, string>? headers = null,
                                                  HttpContent? requestContent = null,
                                                  TimeSpan? timeout = null)
            where TContent : notnull
        {
            return _exchanges.Track(timeout,
                                    () => _inner.ExecuteRequestAsync(dispatcher,
                                                                     url,
                                                                     httpMethod,
                                                                     getResponseContent,
                                                                     successCallback,
                                                                     errorCallback,
                                                                     headers,
                                                                     requestContent,
                                                                     timeout));
        }

        public Task ExecuteRequestAsync(IActorDispatcher dispatcher,
                                        string url,
                                        HttpMethod httpMethod,
                                        Action? successCallback = null,
                                        Action<Exception>? errorCallback = null,
                                        Dictionary<string, string>? headers = null,
                                        HttpContent? requestContent = null,
                                        TimeSpan? timeout = null)
        {
            return _exchanges.Track(timeout,
                                    () => _inner.ExecuteRequestAsync(dispatcher,
                                                                     url,
                                                                     httpMethod,
                                                                     successCallback,
                                                                     errorCallback,
                                                                     headers,
                                                                     requestContent,
                                                                     timeout));
        }

        public Task ExecuteRequestAsync(IActorDispatcher dispatcher,
                                        HttpRequestMessage request,
                                        Action<HttpResponseMessage>? successCallback = null,
                                        Action<Exception>? errorCallback = null,
                                        TimeSpan? timeout = null)
        {
            return _exchanges.Track(timeout, () => _inner.ExecuteRequestAsync(dispatcher, request, successCallback, errorCallback, timeout));
        }
    }
}