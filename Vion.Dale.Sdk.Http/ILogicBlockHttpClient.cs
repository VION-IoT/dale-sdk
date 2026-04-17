using System;
using System.Collections.Generic;
using System.Net.Http;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     Provides non-blocking HTTP client functionality for logic blocks.
    /// </summary>
    [PublicApi]
    public interface ILogicBlockHttpClient
    {
        /// <summary>
        ///     Performs a non-blocking HTTP GET request and passes the deserialized JSON response to the callback.
        /// </summary>
        /// <typeparam name="TResponse">The type to deserialize the JSON response into.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="url">The URL to send the GET request to.</param>
        /// <param name="successCallback">Callback invoked with the deserialized response on success.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     Usually an <see cref="HttpRequestException" /> or <see cref="TimeoutException" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        void GetJson<TResponse>(IActorDispatcher dispatcher,
                                string url,
                                Action<TResponse> successCallback,
                                Action<Exception>? errorCallback = null,
                                Dictionary<string, string>? headers = null,
                                TimeSpan? timeout = null)
            where TResponse : notnull;

        /// <summary>
        ///     Performs a non-blocking HTTP POST request with a JSON body and passes the deserialized JSON response to the callback.
        /// </summary>
        /// <typeparam name="TRequest">The type to serialize as the JSON request body.</typeparam>
        /// <typeparam name="TResponse">The type to deserialize the JSON response into.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="url">The URL to send the POST request to.</param>
        /// <param name="body">The object to serialize as the JSON request body.</param>
        /// <param name="successCallback">Callback invoked with the deserialized response on success.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     Usually an <see cref="HttpRequestException" /> or <see cref="TimeoutException" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        void PostJson<TRequest, TResponse>(IActorDispatcher dispatcher,
                                           string url,
                                           TRequest body,
                                           Action<TResponse> successCallback,
                                           Action<Exception>? errorCallback = null,
                                           Dictionary<string, string>? headers = null,
                                           TimeSpan? timeout = null)
            where TRequest : notnull
            where TResponse : notnull;

        /// <summary>
        ///     Performs a non-blocking HTTP POST request with a JSON body. The callback is invoked on success without a response body.
        /// </summary>
        /// <typeparam name="TRequest">The type to serialize as the JSON request body.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="url">The URL to send the POST request to.</param>
        /// <param name="body">The object to serialize as the JSON request body.</param>
        /// <param name="successCallback">Callback invoked when the request succeeds.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     Usually an <see cref="HttpRequestException" /> or <see cref="TimeoutException" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        void PostJson<TRequest>(IActorDispatcher dispatcher,
                                string url,
                                TRequest body,
                                Action? successCallback = null,
                                Action<Exception>? errorCallback = null,
                                Dictionary<string, string>? headers = null,
                                TimeSpan? timeout = null)
            where TRequest : notnull;

        /// <summary>
        ///     Performs a non-blocking HTTP PUT request with a JSON body and passes the deserialized JSON response to the callback.
        /// </summary>
        /// <typeparam name="TRequest">The type to serialize as the JSON request body.</typeparam>
        /// <typeparam name="TResponse">The type to deserialize the JSON response into.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="url">The URL to send the PUT request to.</param>
        /// <param name="body">The object to serialize as the JSON request body.</param>
        /// <param name="successCallback">Callback invoked with the deserialized response on success.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     Usually an <see cref="HttpRequestException" /> or <see cref="TimeoutException" />.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        void PutJson<TRequest, TResponse>(IActorDispatcher dispatcher,
                                          string url,
                                          TRequest body,
                                          Action<TResponse> successCallback,
                                          Action<Exception>? errorCallback = null,
                                          Dictionary<string, string>? headers = null,
                                          TimeSpan? timeout = null)
            where TRequest : notnull
            where TResponse : notnull;

        /// <summary>
        ///     Performs a non-blocking HTTP PUT request with a JSON body. The callback is invoked on success without a response body.
        /// </summary>
        /// <typeparam name="TRequest">The type to serialize as the JSON request body.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="url">The URL to send the PUT request to.</param>
        /// <param name="body">The object to serialize as the JSON request body.</param>
        /// <param name="successCallback">Callback invoked when the request succeeds.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     Usually an <see cref="HttpRequestException" /> or <see cref="TimeoutException" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        void PutJson<TRequest>(IActorDispatcher dispatcher,
                               string url,
                               TRequest body,
                               Action? successCallback = null,
                               Action<Exception>? errorCallback = null,
                               Dictionary<string, string>? headers = null,
                               TimeSpan? timeout = null)
            where TRequest : notnull;

        /// <summary>
        ///     Performs a non-blocking HTTP DELETE request and passes the deserialized JSON response to the callback.
        /// </summary>
        /// <typeparam name="TResponse">The type to deserialize the JSON response into.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="url">The URL to send the DELETE request to.</param>
        /// <param name="successCallback">Callback invoked with the deserialized response on success.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     Usually an <see cref="HttpRequestException" /> or <see cref="TimeoutException" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        void DeleteJson<TResponse>(IActorDispatcher dispatcher,
                                   string url,
                                   Action<TResponse> successCallback,
                                   Action<Exception>? errorCallback = null,
                                   Dictionary<string, string>? headers = null,
                                   TimeSpan? timeout = null)
            where TResponse : notnull;

        /// <summary>
        ///     Performs a non-blocking HTTP DELETE request. The callback is invoked on success without a response body.
        /// </summary>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="url">The URL to send the DELETE request to.</param>
        /// <param name="successCallback">Callback invoked when the request succeeds.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     Usually an <see cref="HttpRequestException" /> or <see cref="TimeoutException" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        void Delete(IActorDispatcher dispatcher,
                    string url,
                    Action? successCallback = null,
                    Action<Exception>? errorCallback = null,
                    Dictionary<string, string>? headers = null,
                    TimeSpan? timeout = null);

        /// <summary>
        ///     Performs a non-blocking HTTP request and passes the <see cref="HttpResponseMessage" /> to the callback.
        /// </summary>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="request">The <see cref="HttpRequestMessage" /> to send.</param>
        /// <param name="successCallback">Callback invoked with the <see cref="HttpResponseMessage" /> on success.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     Usually an <see cref="HttpRequestException" /> or <see cref="TimeoutException" />.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="timeout">Request-specific timeout that overrides the <see cref="HttpClient" />'s default timeout.</param>
        void SendRequest(IActorDispatcher dispatcher,
                         HttpRequestMessage request,
                         Action<HttpResponseMessage>? successCallback = null,
                         Action<Exception>? errorCallback = null,
                         TimeSpan? timeout = null);
    }
}