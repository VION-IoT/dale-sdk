using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.Cli.Test.TestHelpers
{
    /// <summary>
    ///     Answers every request from a queued script instead of a network, and records what was sent.
    ///     Hand-written rather than mocked: the suite's style is pure functions and real temporary
    ///     directories, and a recording handler is simpler to read than a protected-member setup.
    /// </summary>
    internal sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _answers = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHttpMessageHandler Answer(HttpStatusCode statusCode, string body = "")
        {
            _answers.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) }));
            return this;
        }

        public StubHttpMessageHandler AnswerBy(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        {
            _answers.Enqueue(answer);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_answers.Count == 0)
            {
                throw new InvalidOperationException($"No answer scripted for {request.Method} {request.RequestUri}.");
            }

            return await _answers.Dequeue()(request, cancellationToken);
        }
    }
}