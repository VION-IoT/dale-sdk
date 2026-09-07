using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Http
{
    /// <inheritdoc />
    internal partial class HttpRequestExecutor : IHttpRequestExecutor
    {
        internal const string HttpClientName = "LogicBlockHttpClient";

        /// <summary>
        ///     Names the caller a block author knows. The third overload is reached from
        ///     <c>ILogicBlockHttpClient.SendRequest</c> and from nothing else, so its refusals can say so.
        /// </summary>
        private const string SendRequestOrigin = "Check the request passed to SendRequest.";

        /// <summary>
        ///     The longest per-request timeout the runtime's cancellation source will take. It is that
        ///     source's own bound rather than a policy of ours, and it is restated here because the source
        ///     announces it only by throwing from its constructor. The dispatcher's delay bound
        ///     (<c>LogicBlockBase</c>) comes from the same place and is 294 ms shorter, being the same
        ///     milliseconds truncated to whole seconds; neither is derived from the other.
        /// </summary>
        internal static readonly TimeSpan MaxRequestTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

        private readonly IHttpClientFactory _httpClientFactory;

        private readonly ILogger<HttpRequestExecutor> _logger;

        /// <summary>
        ///     Initializes a new instance of the <see cref="HttpRequestExecutor" /> class.
        /// </summary>
        /// <param name="httpClientFactory">Factory for creating the HTTP client.</param>
        /// <param name="logger">Logger used for logging.</param>
        public HttpRequestExecutor(IHttpClientFactory httpClientFactory, ILogger<HttpRequestExecutor> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <inheritdoc />
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
            RefuseUnusableRequest(dispatcher, timeout, httpMethod, url);

            return SendRequestAsync(dispatcher,
                                    url,
                                    httpMethod,
                                    getResponseContent,
                                    successCallback,
                                    errorCallback,
                                    headers,
                                    requestContent,
                                    timeout);
        }

        /// <inheritdoc />
        public Task ExecuteRequestAsync(IActorDispatcher dispatcher,
                                        string url,
                                        HttpMethod httpMethod,
                                        Action? successCallback = null,
                                        Action<Exception>? errorCallback = null,
                                        Dictionary<string, string>? headers = null,
                                        HttpContent? requestContent = null,
                                        TimeSpan? timeout = null)
        {
            RefuseUnusableRequest(dispatcher, timeout, httpMethod, url);

            return SendRequestAsync(dispatcher,
                                    url,
                                    httpMethod,
                                    successCallback,
                                    errorCallback,
                                    headers,
                                    requestContent,
                                    timeout);
        }

        /// <inheritdoc />
        public Task ExecuteRequestAsync(IActorDispatcher dispatcher,
                                        HttpRequestMessage request,
                                        Action<HttpResponseMessage>? successCallback = null,
                                        Action<Exception>? errorCallback = null,
                                        TimeSpan? timeout = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request), $"{nameof(ExecuteRequestAsync)} was given no request to send. {SendRequestOrigin}");
            }

            if (request.RequestUri == null)
            {
                throw new
                    ArgumentException($"{nameof(ExecuteRequestAsync)} was given a {request.Method} request with no {nameof(HttpRequestMessage.RequestUri)}, so there is nowhere to send it. {SendRequestOrigin}",
                                      nameof(request));
            }

            RefuseUnusableRequest(dispatcher, timeout, request.Method, request.RequestUri.ToString());

            return SendRequestAsync(dispatcher, request, successCallback, errorCallback, timeout);
        }

        private async Task SendRequestAsync<TContent>(IActorDispatcher dispatcher,
                                                      string url,
                                                      HttpMethod httpMethod,
                                                      Func<HttpResponseMessage, Task<TContent>> getResponseContent,
                                                      Action<TContent> successCallback,
                                                      Action<Exception>? errorCallback,
                                                      Dictionary<string, string>? headers,
                                                      HttpContent? requestContent,
                                                      TimeSpan? timeout)
            where TContent : notnull
        {
            LogRequestStarting(httpMethod, url);
            HttpRequestMessage? request = null;
            HttpResponseMessage? response = null;
            using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : new CancellationTokenSource();

            try
            {
                request = CreateRequest(httpMethod, url, requestContent, headers);
                response = await SendAsync(request, cts).ConfigureAwait(false);
                var responseContent = await getResponseContent(response).ConfigureAwait(false);
                LogRequestSucceeded(httpMethod, url, response.StatusCode);
                if (successCallback != null)
                {
                    TryInvokeCallback(dispatcher, () => successCallback(responseContent), httpMethod, url);
                }
            }
            catch (Exception exception)
            {
                HandleException(exception,
                                dispatcher,
                                errorCallback,
                                httpMethod,
                                url,
                                cts,
                                timeout);
            }
            finally
            {
                response?.Dispose();
                request?.Dispose();
            }
        }

        private async Task SendRequestAsync(IActorDispatcher dispatcher,
                                            string url,
                                            HttpMethod httpMethod,
                                            Action? successCallback,
                                            Action<Exception>? errorCallback,
                                            Dictionary<string, string>? headers,
                                            HttpContent? requestContent,
                                            TimeSpan? timeout)
        {
            LogRequestStarting(httpMethod, url);
            HttpRequestMessage? request = null;
            HttpResponseMessage? response = null;
            using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : new CancellationTokenSource();

            try
            {
                request = CreateRequest(httpMethod, url, requestContent, headers);
                response = await SendAsync(request, cts).ConfigureAwait(false);
                LogRequestSucceeded(httpMethod, url, response.StatusCode);
                if (successCallback != null)
                {
                    TryInvokeCallback(dispatcher, successCallback, httpMethod, url);
                }
            }
            catch (Exception exception)
            {
                HandleException(exception,
                                dispatcher,
                                errorCallback,
                                httpMethod,
                                url,
                                cts,
                                timeout);
            }
            finally
            {
                response?.Dispose();
                request?.Dispose();
            }
        }

        private async Task SendRequestAsync(IActorDispatcher dispatcher,
                                            HttpRequestMessage request,
                                            Action<HttpResponseMessage>? successCallback,
                                            Action<Exception>? errorCallback,
                                            TimeSpan? timeout)
        {
            using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : new CancellationTokenSource();
            var url = request.RequestUri.ToString();
            LogRequestStarting(request.Method, url);

            try
            {
                var response = await SendAsync(request, cts).ConfigureAwait(false);
                LogRequestSucceeded(request.Method, url, response.StatusCode);
                if (successCallback != null)
                {
                    TryInvokeCallback(dispatcher, () => successCallback(response), request.Method, url);
                }
                else
                {
                    // This overload has no `finally` on purpose: a callback's response outlives the call and
                    // is that callback's to dispose. Where there is no callback there is no owner either, so
                    // the response would be dropped still holding its body stream.
                    response.Dispose();
                }
            }
            catch (Exception exception)
            {
                HandleException(exception,
                                dispatcher,
                                errorCallback,
                                request.Method,
                                url,
                                cts,
                                timeout);
            }
        }

        /// <summary>
        ///     Refuses, at the caller, the two arguments that would otherwise lose the request without a
        ///     word: a missing dispatcher, whose absence surfaces only as a swallowed callback long after the
        ///     request was sent; and a timeout the cancellation source will not take, whose constructor
        ///     throws before the request is built and therefore outside every <c>catch</c> below.
        ///     <para>
        ///         The message names the method and the URL because a parameter name alone does not tell a
        ///         block author which of its own calls to edit — the same reason the dispatcher names its
        ///         member when it refuses a delay.
        ///     </para>
        /// </summary>
        private static void RefuseUnusableRequest(IActorDispatcher dispatcher, TimeSpan? timeout, HttpMethod httpMethod, string url)
        {
            if (dispatcher == null)
            {
                throw new ArgumentNullException(nameof(dispatcher),
                                                $"{nameof(ExecuteRequestAsync)} was given no dispatcher for the {httpMethod} request to {url}, so neither callback could ever run. " +
                                                "Pass the logic block that should receive them.");
            }

            if (timeout.HasValue && (timeout.Value > MaxRequestTimeout || timeout.Value < Timeout.InfiniteTimeSpan))
            {
                throw new ArgumentOutOfRangeException(nameof(timeout),
                                                      timeout.Value,
                                                      $"{nameof(ExecuteRequestAsync)} cannot bound the {httpMethod} request to {url} with this timeout. " +
                                                      $"A request timeout is {Timeout.InfiniteTimeSpan} for no bound, or from {TimeSpan.Zero} to {MaxRequestTimeout}.");
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod httpMethod, string url, HttpContent? requestContent, Dictionary<string, string>? headers)
        {
            var request = new HttpRequestMessage(httpMethod, url) { Content = requestContent };
            if (headers == null)
            {
                return request;
            }

            foreach (var header in headers)
            {
                if (request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    LogHeaderAdded(header.Key, request.Method, request.RequestUri);
                }
                else
                {
                    LogHeaderAddFailed(header.Key, request.Method, request.RequestUri);
                }
            }

            return request;
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationTokenSource cts)
        {
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            HttpResponseMessage response;

            try
            {
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cts.IsCancellationRequested && exception.InnerException is TimeoutException)
            {
                /* The client's own bound elapsed, and both clauses are needed to know that. Three things
                 * can cancel this call — the per-request source, a handler in the named client's pipeline
                 * cancelling on a token of its own, and the client's timeout — and all three arrive as an
                 * OperationCanceledException, so the class caught says only that one of them happened. The
                 * first is what the per-request source's own flag excludes; the other two are told apart by
                 * the inner exception, which the client sets to a TimeoutException only for its own bound. Reading the flag alone hands a handler's cancellation to the block
                 * as a timeout naming the client's bound, for an exchange that never reached it — wrapping a
                 * transport failure `AC-HTTP-006.1` says arrives as the handler threw it.
                 *
                 * The relabel is here rather than beside the per-request one in HandleException so that the
                 * bound can be named from the client that imposed it, which is a local of this method; the
                 * failure path holds only the factory, and would have to build a second client to read it. */
                throw TimedOut(httpClient.Timeout);
            }

            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                // The status is judged on the headers, so this response still holds a body stream nobody
                // will drain: no callback receives a failed response, and the caller never sees the local.
                // Disposing here reaches all three overloads at once, which is why neither the two `finally`
                // blocks nor the third overload (whose response the callback owns) needs to change.
                response.Dispose();

                throw;
            }

            return response;
        }

        private void HandleException(Exception exception,
                                     IActorDispatcher dispatcher,
                                     Action<Exception>? callback,
                                     HttpMethod httpMethod,
                                     string url,
                                     CancellationTokenSource cts,
                                     TimeSpan? timeout)
        {
            // Only a cancellation is a timeout. The source's state alone does not say what failed: the body
            // read and the deserialization run outside the token, so a JsonException, an IOException or a
            // non-success status raised once the bound has elapsed would otherwise reach the block as
            // "Timed out after n seconds" with nothing left of what actually went wrong.
            if (exception is OperationCanceledException && cts.IsCancellationRequested && timeout != null)
            {
                exception = TimedOut(timeout.Value);
            }

            LogRequestFailed(exception, httpMethod, url);
            if (callback != null)
            {
                TryInvokeCallback(dispatcher, () => callback(exception), httpMethod, url);
            }
        }

        /// <summary>
        ///     Builds the timeout for either bound — the one exception class this package mints rather than
        ///     passes through from the transport. The number is rendered invariantly rather than in the
        ///     machine's culture: this message is what a block author matches on and what a support engineer
        ///     greps for in a gateway log, and the gateways are German-locale, where a culture-rendered
        ///     <c>0.05</c> reads <c>0,05</c> and no query for the one finds the other. Both bounds are named
        ///     through this one rendering, so the two messages cannot drift apart.
        /// </summary>
        private static TimeoutException TimedOut(TimeSpan bound)
        {
            return new TimeoutException($"Timed out after {bound.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds");
        }

        private void TryInvokeCallback(IActorDispatcher dispatcher, Action callback, HttpMethod httpMethod, string url)
        {
            try
            {
                dispatcher.InvokeSynchronized(callback);
            }
            catch (Exception exception)
            {
                LogCallbackFailed(exception, httpMethod, url);
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Starting HTTP {HttpMethod} request to {Url}")]
        private partial void LogRequestStarting(HttpMethod httpMethod, string url);

        [LoggerMessage(Level = LogLevel.Debug, Message = "HTTP {HttpMethod} request to {Url} succeeded with status {StatusCode}")]
        private partial void LogRequestSucceeded(HttpMethod httpMethod, string url, HttpStatusCode statusCode);

        [LoggerMessage(Level = LogLevel.Error, Message = "HTTP {HttpMethod} request to {Url} failed")]
        private partial void LogRequestFailed(Exception exception, HttpMethod httpMethod, string url);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Added header {HeaderKey} for {HttpMethod} request to {Url}")]
        private partial void LogHeaderAdded(string headerKey, HttpMethod httpMethod, Uri url);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to add header {HeaderKey} for {HttpMethod} request to {Url} - may already exist or be invalid")]
        private partial void LogHeaderAddFailed(string headerKey, HttpMethod httpMethod, Uri url);

        // "actor may be disposed" named the wrong cause. The dispatcher refuses a self-send while the block
        // has not yet received its first message, and that refusal - which says so, and says where to
        // schedule from instead - is the inner exception here far more often than a disposal is.
        [LoggerMessage(Level = LogLevel.Error,
                       Message =
                           "Could not hand the callback for the {HttpMethod} request to {Url} to the block - it may not have received its first message yet, or may already have stopped. The request's outcome reached nobody")]
        private partial void LogCallbackFailed(Exception exception, HttpMethod httpMethod, string url);
    }
}