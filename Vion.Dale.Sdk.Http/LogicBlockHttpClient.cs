using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Http
{
    /// <inheritdoc />
    public partial class LogicBlockHttpClient : ILogicBlockHttpClient
    {
        private readonly IHttpContentSerializer _httpContentSerializer;

        private readonly IHttpRequestExecutor _httpRequestExecutor;

        private readonly ILogger<LogicBlockHttpClient> _logger;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LogicBlockHttpClient" /> class.
        /// </summary>
        /// <param name="httpRequestExecutor">The executor responsible for performing HTTP requests.</param>
        /// <param name="httpContentSerializer">The serializer for converting HTTP content to and from different formats.</param>
        /// <param name="logger">The logger used for logging.</param>
        public LogicBlockHttpClient(IHttpRequestExecutor httpRequestExecutor, IHttpContentSerializer httpContentSerializer, ILogger<LogicBlockHttpClient> logger)
        {
            _httpRequestExecutor = httpRequestExecutor;
            _httpContentSerializer = httpContentSerializer;
            _logger = logger;
        }

        /// <inheritdoc />
        public void GetJson<TResponse>(IActorDispatcher dispatcher,
                                       string url,
                                       Action<TResponse> successCallback,
                                       Action<Exception>? errorCallback = null,
                                       Dictionary<string, string>? headers = null,
                                       TimeSpan? timeout = null)
            where TResponse : notnull
        {
            var httpMethod = HttpMethod.Get;
            var task = _httpRequestExecutor.ExecuteRequestAsync(dispatcher,
                                                                url,
                                                                httpMethod,
                                                                response => _httpContentSerializer.DeserializeJsonAsync<TResponse>(response.Content),
                                                                successCallback,
                                                                errorCallback,
                                                                headers,
                                                                null,
                                                                timeout);
            CreateExceptionContinuation(task, httpMethod, url);
        }

        /// <inheritdoc />
        public void PostJson<TRequest, TResponse>(IActorDispatcher dispatcher,
                                                  string url,
                                                  TRequest body,
                                                  Action<TResponse> successCallback,
                                                  Action<Exception>? errorCallback = null,
                                                  Dictionary<string, string>? headers = null,
                                                  TimeSpan? timeout = null)
            where TRequest : notnull
            where TResponse : notnull
        {
            var httpMethod = HttpMethod.Post;
            var requestContent = _httpContentSerializer.SerializeJson(body);
            var task = _httpRequestExecutor.ExecuteRequestAsync(dispatcher,
                                                                url,
                                                                httpMethod,
                                                                response => _httpContentSerializer.DeserializeJsonAsync<TResponse>(response.Content),
                                                                successCallback,
                                                                errorCallback,
                                                                headers,
                                                                requestContent,
                                                                timeout);
            CreateExceptionContinuation(task, httpMethod, url);
        }

        /// <inheritdoc />
        public void PostJson<TRequest>(IActorDispatcher dispatcher,
                                       string url,
                                       TRequest body,
                                       Action? successCallback = null,
                                       Action<Exception>? errorCallback = null,
                                       Dictionary<string, string>? headers = null,
                                       TimeSpan? timeout = null)
            where TRequest : notnull
        {
            var httpMethod = HttpMethod.Post;
            var requestContent = _httpContentSerializer.SerializeJson(body);
            var task = _httpRequestExecutor.ExecuteRequestAsync(dispatcher,
                                                                url,
                                                                httpMethod,
                                                                successCallback,
                                                                errorCallback,
                                                                headers,
                                                                requestContent,
                                                                timeout);
            CreateExceptionContinuation(task, httpMethod, url);
        }

        /// <inheritdoc />
        public void PutJson<TRequest, TResponse>(IActorDispatcher dispatcher,
                                                 string url,
                                                 TRequest body,
                                                 Action<TResponse> successCallback,
                                                 Action<Exception>? errorCallback = null,
                                                 Dictionary<string, string>? headers = null,
                                                 TimeSpan? timeout = null)
            where TRequest : notnull
            where TResponse : notnull
        {
            var httpMethod = HttpMethod.Put;
            var requestContent = _httpContentSerializer.SerializeJson(body);
            var task = _httpRequestExecutor.ExecuteRequestAsync(dispatcher,
                                                                url,
                                                                httpMethod,
                                                                response => _httpContentSerializer.DeserializeJsonAsync<TResponse>(response.Content),
                                                                successCallback,
                                                                errorCallback,
                                                                headers,
                                                                requestContent,
                                                                timeout);
            CreateExceptionContinuation(task, httpMethod, url);
        }

        /// <inheritdoc />
        public void PutJson<TRequest>(IActorDispatcher dispatcher,
                                      string url,
                                      TRequest body,
                                      Action? successCallback = null,
                                      Action<Exception>? errorCallback = null,
                                      Dictionary<string, string>? headers = null,
                                      TimeSpan? timeout = null)
            where TRequest : notnull
        {
            var httpMethod = HttpMethod.Put;
            var requestContent = _httpContentSerializer.SerializeJson(body);
            var task = _httpRequestExecutor.ExecuteRequestAsync(dispatcher,
                                                                url,
                                                                httpMethod,
                                                                successCallback,
                                                                errorCallback,
                                                                headers,
                                                                requestContent,
                                                                timeout);
            CreateExceptionContinuation(task, httpMethod, url);
        }

        /// <inheritdoc />
        public void DeleteJson<TResponse>(IActorDispatcher dispatcher,
                                          string url,
                                          Action<TResponse> successCallback,
                                          Action<Exception>? errorCallback = null,
                                          Dictionary<string, string>? headers = null,
                                          TimeSpan? timeout = null)
            where TResponse : notnull
        {
            var httpMethod = HttpMethod.Delete;
            var task = _httpRequestExecutor.ExecuteRequestAsync(dispatcher,
                                                                url,
                                                                httpMethod,
                                                                response => _httpContentSerializer.DeserializeJsonAsync<TResponse>(response.Content),
                                                                successCallback,
                                                                errorCallback,
                                                                headers,
                                                                null,
                                                                timeout);
            CreateExceptionContinuation(task, httpMethod, url);
        }

        /// <inheritdoc />
        public void Delete(IActorDispatcher dispatcher,
                           string url,
                           Action? successCallback = null,
                           Action<Exception>? errorCallback = null,
                           Dictionary<string, string>? headers = null,
                           TimeSpan? timeout = null)
        {
            var httpMethod = HttpMethod.Delete;
            var task = _httpRequestExecutor.ExecuteRequestAsync(dispatcher,
                                                                url,
                                                                httpMethod,
                                                                successCallback,
                                                                errorCallback,
                                                                headers,
                                                                null,
                                                                timeout);
            CreateExceptionContinuation(task, httpMethod, url);
        }

        /// <inheritdoc />
        public void SendRequest(IActorDispatcher dispatcher,
                                HttpRequestMessage request,
                                Action<HttpResponseMessage>? successCallback = null,
                                Action<Exception>? errorCallback = null,
                                TimeSpan? timeout = null)
        {
            var task = _httpRequestExecutor.ExecuteRequestAsync(dispatcher, request, successCallback, errorCallback, timeout);
            CreateExceptionContinuation(task, request.Method, request.RequestUri.ToString());
        }

        private void CreateExceptionContinuation(Task task, HttpMethod httpMethod, string url)
        {
            // Ensures task exceptions are logged even if unobserved
            task.ContinueWith(t =>
                              {
                                  if (t.Exception != null)
                                  {
                                      LogUnhandledException(t.Exception, httpMethod, url);
                                  }
                              },
                              TaskContinuationOptions.OnlyOnFaulted);
        }

        [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in HTTP {HttpMethod} request task to {Url}")]
        private partial void LogUnhandledException(Exception exception, HttpMethod httpMethod, string url);
    }
}