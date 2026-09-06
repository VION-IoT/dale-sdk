using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.Sdk.Http.Test.TestHelpers
{
    /// <summary>
    ///     The seam every test in this project reaches production through: the innermost
    ///     <see cref="HttpMessageHandler" /> of a real <see cref="HttpClient" />, so the request under
    ///     assertion is the one the package actually composed. Nothing here opens a socket.
    ///     <para>
    ///         Two limits of this seam are contract, not accident, and the classes that rely on them say so:
    ///         a 3xx is <b>not</b> followed, because the handler that follows redirects is the very one this
    ///         replaces; and a <c>Content-Length</c> that disagrees with the body is accepted, where a real
    ///         <c>SocketsHttpHandler</c> fails the read.
    ///     </para>
    /// </summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        /// <summary>Every request the package sent through this handler, in the order it sent them.</summary>
        public List<HttpRequestMessage> Requests { get; } = new();

        /// <summary>The request the package sent last, or <c>null</c> when it sent none.</summary>
        public HttpRequestMessage? LastRequest
        {
            get => Requests.Count == 0 ? null : Requests[Requests.Count - 1];
        }

        private StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        /// <summary>Answers every request with <paramref name="statusCode" /> and an optional JSON body.</summary>
        public static StubHttpMessageHandler Answering(HttpStatusCode statusCode, string? jsonBody = null)
        {
            return new StubHttpMessageHandler((_, _) => Task.FromResult(Respond(statusCode, jsonBody)));
        }

        /// <summary>Answers every request with the same <see cref="HttpResponseMessage" /> instance.</summary>
        public static StubHttpMessageHandler Returning(HttpResponseMessage response)
        {
            return new StubHttpMessageHandler((_, _) => Task.FromResult(response));
        }

        /// <summary>Throws <paramref name="exception" /> synchronously, the way a transport failure surfaces.</summary>
        public static StubHttpMessageHandler Throwing(Exception exception)
        {
            return new StubHttpMessageHandler((_, _) => throw exception);
        }

        /// <summary>
        ///     Never answers, and honours the cancellation token — the discriminator for every timeout claim.
        ///     A handler that ignores the token reads the opposite of a real one on a timeout of zero.
        /// </summary>
        public static StubHttpMessageHandler NeverCompleting()
        {
            return new StubHttpMessageHandler((_, cancellationToken) =>
                                              {
                                                  var pending = new TaskCompletionSource<HttpResponseMessage>();
                                                  cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));

                                                  return pending.Task;
                                              });
        }

        /// <summary>Answers each request from <paramref name="respond" />, which sees the request and the token.</summary>
        public static StubHttpMessageHandler Responding(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            return new StubHttpMessageHandler(respond);
        }

        /// <summary>A response carrying <paramref name="jsonBody" />, or an empty body when it is <c>null</c>.</summary>
        public static HttpResponseMessage Respond(HttpStatusCode statusCode, string? jsonBody)
        {
            return new HttpResponseMessage(statusCode)
                   {
                       Content = jsonBody == null ? new StringContent(string.Empty) :
                                     new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                   };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return _respond(request, cancellationToken);
        }
    }
}