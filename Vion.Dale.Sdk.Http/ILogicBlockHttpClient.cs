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
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="url">The URL to send the GET request to.</param>
        /// <param name="successCallback">Callback invoked with the deserialized response on success.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     One class per failure: <see cref="HttpRequestException" /> for a non-success status or a
        ///     transport failure the handler wrapped, <see cref="TimeoutException" /> when the
        ///     <c>timeout</c> above elapsed, <see cref="System.Threading.Tasks.TaskCanceledException" />
        ///     when the <see cref="HttpClient" />'s own timeout did,
        ///     <see cref="InvalidOperationException" /> for a URL that is not an absolute URI,
        ///     <see cref="System.Text.Json.JsonException" /> for a body that is absent or malformed,
        ///     <see cref="ContentNullAfterDeserializationException" /> for one that deserializes to null,
        ///     and otherwise whatever the transport threw — this client wraps nothing else.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">
        ///     A bound on this request alone, applied <i>in addition to</i> the <see cref="HttpClient" />'s own
        ///     timeout rather than in place of it: whichever elapses first ends the request, so a value longer
        ///     than the client's does not extend it. Its expiry arrives as a <see cref="TimeoutException" />,
        ///     where the client's own arrives as a <see cref="System.Threading.Tasks.TaskCanceledException" />.
        /// </param>
        void GetJson<TResponse>(IActorDispatcher dispatcher,
                                string url,
                                Action<TResponse> successCallback,
                                Action<Exception>? errorCallback = null,
                                Dictionary<string, string>? headers = null,
                                TimeSpan? timeout = null)
            where TResponse : notnull;

        /// <summary>
        ///     Performs a non-blocking HTTP POST request with a JSON body and passes the deserialized JSON response to the
        ///     callback.
        /// </summary>
        /// <typeparam name="TRequest">The type to serialize as the JSON request body.</typeparam>
        /// <typeparam name="TResponse">The type to deserialize the JSON response into.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="url">The URL to send the POST request to.</param>
        /// <param name="body">The object to serialize as the JSON request body.</param>
        /// <param name="successCallback">Callback invoked with the deserialized response on success.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     One class per failure: <see cref="HttpRequestException" /> for a non-success status or a
        ///     transport failure the handler wrapped, <see cref="TimeoutException" /> when the
        ///     <c>timeout</c> above elapsed, <see cref="System.Threading.Tasks.TaskCanceledException" />
        ///     when the <see cref="HttpClient" />'s own timeout did,
        ///     <see cref="InvalidOperationException" /> for a URL that is not an absolute URI,
        ///     <see cref="System.Text.Json.JsonException" /> for a body that is absent or malformed,
        ///     <see cref="ContentNullAfterDeserializationException" /> for one that deserializes to null,
        ///     and otherwise whatever the transport threw — this client wraps nothing else.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">
        ///     A bound on this request alone, applied <i>in addition to</i> the <see cref="HttpClient" />'s own
        ///     timeout rather than in place of it: whichever elapses first ends the request, so a value longer
        ///     than the client's does not extend it. Its expiry arrives as a <see cref="TimeoutException" />,
        ///     where the client's own arrives as a <see cref="System.Threading.Tasks.TaskCanceledException" />.
        /// </param>
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
        ///     Performs a non-blocking HTTP POST request with a JSON body. The callback is invoked on success without a response
        ///     body.
        /// </summary>
        /// <typeparam name="TRequest">The type to serialize as the JSON request body.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="url">The URL to send the POST request to.</param>
        /// <param name="body">The object to serialize as the JSON request body.</param>
        /// <param name="successCallback">Callback invoked when the request succeeds.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     One class per failure: <see cref="HttpRequestException" /> for a non-success status or a
        ///     transport failure the handler wrapped, <see cref="TimeoutException" /> when the
        ///     <c>timeout</c> above elapsed, <see cref="System.Threading.Tasks.TaskCanceledException" />
        ///     when the <see cref="HttpClient" />'s own timeout did,
        ///     <see cref="InvalidOperationException" /> for a URL that is not an absolute URI,
        ///     <see cref="System.Text.Json.JsonException" /> for a body that is absent or malformed,
        ///     <see cref="ContentNullAfterDeserializationException" /> for one that deserializes to null,
        ///     and otherwise whatever the transport threw — this client wraps nothing else.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">
        ///     A bound on this request alone, applied <i>in addition to</i> the <see cref="HttpClient" />'s own
        ///     timeout rather than in place of it: whichever elapses first ends the request, so a value longer
        ///     than the client's does not extend it. Its expiry arrives as a <see cref="TimeoutException" />,
        ///     where the client's own arrives as a <see cref="System.Threading.Tasks.TaskCanceledException" />.
        /// </param>
        void PostJson<TRequest>(IActorDispatcher dispatcher,
                                string url,
                                TRequest body,
                                Action? successCallback = null,
                                Action<Exception>? errorCallback = null,
                                Dictionary<string, string>? headers = null,
                                TimeSpan? timeout = null)
            where TRequest : notnull;

        /// <summary>
        ///     Performs a non-blocking HTTP PUT request with a JSON body and passes the deserialized JSON response to the
        ///     callback.
        /// </summary>
        /// <typeparam name="TRequest">The type to serialize as the JSON request body.</typeparam>
        /// <typeparam name="TResponse">The type to deserialize the JSON response into.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="url">The URL to send the PUT request to.</param>
        /// <param name="body">The object to serialize as the JSON request body.</param>
        /// <param name="successCallback">Callback invoked with the deserialized response on success.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     One class per failure: <see cref="HttpRequestException" /> for a non-success status or a
        ///     transport failure the handler wrapped, <see cref="TimeoutException" /> when the
        ///     <c>timeout</c> above elapsed, <see cref="System.Threading.Tasks.TaskCanceledException" />
        ///     when the <see cref="HttpClient" />'s own timeout did,
        ///     <see cref="InvalidOperationException" /> for a URL that is not an absolute URI,
        ///     <see cref="System.Text.Json.JsonException" /> for a body that is absent or malformed,
        ///     <see cref="ContentNullAfterDeserializationException" /> for one that deserializes to null,
        ///     and otherwise whatever the transport threw — this client wraps nothing else.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">
        ///     A bound on this request alone, applied <i>in addition to</i> the <see cref="HttpClient" />'s own
        ///     timeout rather than in place of it: whichever elapses first ends the request, so a value longer
        ///     than the client's does not extend it. Its expiry arrives as a <see cref="TimeoutException" />,
        ///     where the client's own arrives as a <see cref="System.Threading.Tasks.TaskCanceledException" />.
        /// </param>
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
        ///     Performs a non-blocking HTTP PUT request with a JSON body. The callback is invoked on success without a response
        ///     body.
        /// </summary>
        /// <typeparam name="TRequest">The type to serialize as the JSON request body.</typeparam>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="url">The URL to send the PUT request to.</param>
        /// <param name="body">The object to serialize as the JSON request body.</param>
        /// <param name="successCallback">Callback invoked when the request succeeds.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     One class per failure: <see cref="HttpRequestException" /> for a non-success status or a
        ///     transport failure the handler wrapped, <see cref="TimeoutException" /> when the
        ///     <c>timeout</c> above elapsed, <see cref="System.Threading.Tasks.TaskCanceledException" />
        ///     when the <see cref="HttpClient" />'s own timeout did,
        ///     <see cref="InvalidOperationException" /> for a URL that is not an absolute URI,
        ///     <see cref="System.Text.Json.JsonException" /> for a body that is absent or malformed,
        ///     <see cref="ContentNullAfterDeserializationException" /> for one that deserializes to null,
        ///     and otherwise whatever the transport threw — this client wraps nothing else.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">
        ///     A bound on this request alone, applied <i>in addition to</i> the <see cref="HttpClient" />'s own
        ///     timeout rather than in place of it: whichever elapses first ends the request, so a value longer
        ///     than the client's does not extend it. Its expiry arrives as a <see cref="TimeoutException" />,
        ///     where the client's own arrives as a <see cref="System.Threading.Tasks.TaskCanceledException" />.
        /// </param>
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
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="url">The URL to send the DELETE request to.</param>
        /// <param name="successCallback">Callback invoked with the deserialized response on success.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     One class per failure: <see cref="HttpRequestException" /> for a non-success status or a
        ///     transport failure the handler wrapped, <see cref="TimeoutException" /> when the
        ///     <c>timeout</c> above elapsed, <see cref="System.Threading.Tasks.TaskCanceledException" />
        ///     when the <see cref="HttpClient" />'s own timeout did,
        ///     <see cref="InvalidOperationException" /> for a URL that is not an absolute URI,
        ///     <see cref="System.Text.Json.JsonException" /> for a body that is absent or malformed,
        ///     <see cref="ContentNullAfterDeserializationException" /> for one that deserializes to null,
        ///     and otherwise whatever the transport threw — this client wraps nothing else.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">
        ///     A bound on this request alone, applied <i>in addition to</i> the <see cref="HttpClient" />'s own
        ///     timeout rather than in place of it: whichever elapses first ends the request, so a value longer
        ///     than the client's does not extend it. Its expiry arrives as a <see cref="TimeoutException" />,
        ///     where the client's own arrives as a <see cref="System.Threading.Tasks.TaskCanceledException" />.
        /// </param>
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
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="url">The URL to send the DELETE request to.</param>
        /// <param name="successCallback">Callback invoked when the request succeeds.</param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     One class per failure: <see cref="HttpRequestException" /> for a non-success status or a
        ///     transport failure the handler wrapped, <see cref="TimeoutException" /> when the
        ///     <c>timeout</c> above elapsed, <see cref="System.Threading.Tasks.TaskCanceledException" />
        ///     when the <see cref="HttpClient" />'s own timeout did,
        ///     <see cref="InvalidOperationException" /> for a URL that is not an absolute URI,
        ///     <see cref="System.Text.Json.JsonException" /> for a body that is absent or malformed,
        ///     <see cref="ContentNullAfterDeserializationException" /> for one that deserializes to null,
        ///     and otherwise whatever the transport threw — this client wraps nothing else.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="headers">HTTP headers to include in the request.</param>
        /// <param name="timeout">
        ///     A bound on this request alone, applied <i>in addition to</i> the <see cref="HttpClient" />'s own
        ///     timeout rather than in place of it: whichever elapses first ends the request, so a value longer
        ///     than the client's does not extend it. Its expiry arrives as a <see cref="TimeoutException" />,
        ///     where the client's own arrives as a <see cref="System.Threading.Tasks.TaskCanceledException" />.
        /// </param>
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
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="request">
        ///     The <see cref="HttpRequestMessage" /> to send. It stays yours: this member does not dispose
        ///     it, and its method, URI, headers and content are the ones sent — no URL or header parameter
        ///     of this member applies, and no content type is set for you.
        /// </param>
        /// <param name="successCallback">
        ///     Callback invoked with the <see cref="HttpResponseMessage" /> on success. The response is
        ///     <b>yours to read and to dispose</b>: unlike the members that carry a response type, this one
        ///     disposes nothing, and the callback may be reached while the body is still arriving, because
        ///     the response is handed over as soon as its headers are in.
        /// </param>
        /// <param name="errorCallback">
        ///     Callback invoked with the exception if the request fails.
        ///     One class per failure: <see cref="HttpRequestException" /> for a non-success status or a
        ///     transport failure the handler wrapped, <see cref="TimeoutException" /> when the
        ///     <c>timeout</c> above elapsed, <see cref="System.Threading.Tasks.TaskCanceledException" />
        ///     when the <see cref="HttpClient" />'s own timeout did,
        ///     <see cref="InvalidOperationException" /> for a URL that is not an absolute URI,
        ///     <see cref="System.Text.Json.JsonException" /> for a body that is absent or malformed,
        ///     <see cref="ContentNullAfterDeserializationException" /> for one that deserializes to null,
        ///     and otherwise whatever the transport threw — this client wraps nothing else.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="timeout">
        ///     A bound on this request alone, applied <i>in addition to</i> the <see cref="HttpClient" />'s own
        ///     timeout rather than in place of it: whichever elapses first ends the request, so a value longer
        ///     than the client's does not extend it. Its expiry arrives as a <see cref="TimeoutException" />,
        ///     where the client's own arrives as a <see cref="System.Threading.Tasks.TaskCanceledException" />.
        /// </param>
        void SendRequest(IActorDispatcher dispatcher,
                         HttpRequestMessage request,
                         Action<HttpResponseMessage>? successCallback = null,
                         Action<Exception>? errorCallback = null,
                         TimeSpan? timeout = null);
    }
}