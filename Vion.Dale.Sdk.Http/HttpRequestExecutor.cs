using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Http
{
    /// <inheritdoc />
    internal partial class HttpRequestExecutor : IHttpRequestExecutor
    {
        internal const string HttpClientName = "LogicBlockHttpClient";

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
        public async Task ExecuteRequestAsync<TContent>(IActorDispatcher dispatcher,
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
                TryInvokeCallback(dispatcher, () => successCallback(responseContent), httpMethod, url);
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

        /// <inheritdoc />
        public async Task ExecuteRequestAsync(IActorDispatcher dispatcher,
                                              string url,
                                              HttpMethod httpMethod,
                                              Action? successCallback = null,
                                              Action<Exception>? errorCallback = null,
                                              Dictionary<string, string>? headers = null,
                                              HttpContent? requestContent = null,
                                              TimeSpan? timeout = null)
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

        /// <inheritdoc />
        public async Task ExecuteRequestAsync(IActorDispatcher dispatcher,
                                              HttpRequestMessage request,
                                              Action<HttpResponseMessage>? successCallback = null,
                                              Action<Exception>? errorCallback = null,
                                              TimeSpan? timeout = null)
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
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

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
            if (cts.IsCancellationRequested && timeout != null)
            {
                exception = new TimeoutException($"Timed out after {timeout.Value.TotalSeconds} seconds");
            }

            LogRequestFailed(exception, httpMethod, url);
            if (callback != null)
            {
                TryInvokeCallback(dispatcher, () => callback(exception), httpMethod, url);
            }
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

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to invoke callback for {HttpMethod} request to {Url} - actor may be disposed")]
        private partial void LogCallbackFailed(Exception exception, HttpMethod httpMethod, string url);
    }
}