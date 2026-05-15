using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     Executes HTTP requests and marshals responses back to the actor's synchronization context.
    /// </summary>
    public interface IHttpRequestExecutor
    {
        /// <summary>
        ///     Executes an HTTP request and passes the deserialized response content to the callback.
        /// </summary>
        /// <typeparam name="TContent">The type of the response content.</typeparam>
        /// <param name="dispatcher">The dispatcher that will invoke the callbacks.</param>
        /// <param name="url">The URL to send the HTTP request to.</param>
        /// <param name="httpMethod">The HTTP method to use.</param>
        /// <param name="getResponseContent">Function to extract and deserialize content from the HTTP response.</param>
        /// <param name="successCallback">Callback invoked with the deserialized response content on success.</param>
        /// <param name="errorCallback">Callback invoked with the exception if the request fails.</param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="requestContent">The HTTP content to send in the request body.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ExecuteRequestAsync<TContent>(IActorDispatcher dispatcher,
                                           string url,
                                           HttpMethod httpMethod,
                                           Func<HttpResponseMessage, Task<TContent>> getResponseContent,
                                           Action<TContent> successCallback,
                                           Action<Exception>? errorCallback = null,
                                           Dictionary<string, string>? headers = null,
                                           HttpContent? requestContent = null,
                                           TimeSpan? timeout = null)
            where TContent : notnull;

        /// <summary>
        ///     Executes an HTTP request without processing response content. The callback is invoked on success.
        /// </summary>
        /// <param name="dispatcher">The dispatcher that will invoke the callbacks.</param>
        /// <param name="url">The URL to send the HTTP request to.</param>
        /// <param name="httpMethod">The HTTP method to use.</param>
        /// <param name="successCallback">Callback invoked when the request succeeds.</param>
        /// <param name="errorCallback">Callback invoked with the exception if the request fails.</param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="requestContent">The HTTP content to send in the request body.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ExecuteRequestAsync(IActorDispatcher dispatcher,
                                 string url,
                                 HttpMethod httpMethod,
                                 Action? successCallback = null,
                                 Action<Exception>? errorCallback = null,
                                 Dictionary<string, string>? headers = null,
                                 HttpContent? requestContent = null,
                                 TimeSpan? timeout = null);

        /// <summary>
        ///     Executes an HTTP request and passes the <see cref="HttpResponseMessage" /> to the callback.
        /// </summary>
        /// <param name="dispatcher">The dispatcher that will invoke the callbacks.</param>
        /// <param name="request">The <see cref="HttpRequestMessage" /> to send.</param>
        /// <param name="successCallback">Callback invoked with the <see cref="HttpResponseMessage" /> on success.</param>
        /// <param name="errorCallback">Callback invoked with the exception if the request fails.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ExecuteRequestAsync(IActorDispatcher dispatcher,
                                 HttpRequestMessage request,
                                 Action<HttpResponseMessage>? successCallback = null,
                                 Action<Exception>? errorCallback = null,
                                 TimeSpan? timeout = null);
    }
}