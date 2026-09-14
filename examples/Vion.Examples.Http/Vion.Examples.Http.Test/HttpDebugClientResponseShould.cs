using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Vion.Examples.Http.LogicBlocks;
using Xunit;

namespace Vion.Examples.Http.Test
{
    /// <summary>
    ///     The response pane. Every answer is scripted through the HTTP test kit, so it reaches the block through the SDK's
    ///     own status judgement, timeout and actor hop. An answer only hands the callback to the block's dispatcher, and a
    ///     2xx body is handed back once more after it is read, so each test drives the context before it asserts.
    /// </summary>
    public sealed class HttpDebugClientResponseShould : IDisposable
    {
        public void Dispose()
        {
            _fixture.Dispose();
        }

        private readonly DebugClientFixture _fixture = new();

        private HttpDebugClient Sut
        {
            get => _fixture.Sut;
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        public void ReportStatusOfNonSuccessResponse(HttpStatusCode status)
        {
            // Arrange
            var ctx = _fixture.Build();
            Sut.SendOnce = true;

            // Act
            _fixture.Harness.Respond(status, "detail the block never sees", "text/plain");
            ctx.FlushPendingActions();

            // Assert
            Assert.Equal(RequestOutcome.HttpError, Sut.Outcome);
            Assert.Equal((int)status, Sut.StatusCode);
            Assert.Empty(Sut.ResponseBodyPreview);
            Assert.Equal($"The server answered {(int)status}. Only a 2xx response reaches this block with its headers and body.", Sut.LastError);
        }

        [Fact]
        public void KeepBodyThatExactlyFillsPreview()
        {
            // Arrange
            var ctx = _fixture.Build();
            Sut.SendOnce = true;

            // Act
            _fixture.Harness.Respond(HttpStatusCode.OK, new string('x', 8192), "text/plain");
            ctx.AdvanceTime(TimeSpan.Zero);

            // Assert
            Assert.False(Sut.ResponseBodyTruncated);
            Assert.Equal(8192, Sut.ResponseBodyPreview.Length);
        }

        [Fact]
        public void MeasureLatencyOnBlockClock()
        {
            // Arrange
            var ctx = _fixture.Build();
            Sut.SendOnce = true;
            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250));

            // Act
            _fixture.Harness.Respond("{}");
            ctx.AdvanceTime(TimeSpan.Zero);

            // Assert
            Assert.Equal(250, Sut.LatencyMs);
        }

        [Fact]
        public void ReportTimeoutWhenNoAnswerArrives()
        {
            // Arrange
            var ctx = _fixture.Build();
            Sut.Timeout = TimeSpan.FromSeconds(5);
            Sut.SendOnce = true;
            ctx.AdvanceTime(TimeSpan.FromSeconds(4));
            var outcomeBeforeBound = Sut.Outcome;

            // Act
            ctx.AdvanceTime(TimeSpan.FromSeconds(1));
            ctx.FlushPendingActions();

            // Assert
            Assert.Equal(RequestOutcome.InFlight, outcomeBeforeBound);
            Assert.Equal(RequestOutcome.TimedOut, Sut.Outcome);
            Assert.Null(Sut.StatusCode);
            Assert.Equal(5000, Sut.LatencyMs);
            Assert.Equal("Timed out after 5 seconds", Sut.LastError);
        }

        [Fact]
        public void ReportTransportFailureWithCause()
        {
            // Arrange
            var ctx = _fixture.Build();
            Sut.SendOnce = true;
            var refused = new HttpRequestException("An error occurred while sending the request.", new SocketException((int)SocketError.ConnectionRefused));

            // Act
            _fixture.Harness.Fail(refused);
            ctx.FlushPendingActions();

            // Assert
            Assert.Equal(RequestOutcome.Failed, Sut.Outcome);
            Assert.Null(Sut.StatusCode);
            Assert.Equal($"An error occurred while sending the request. → {refused.InnerException!.Message}", Sut.LastError);
        }

        [Fact]
        public void ShowSuccessfulResponse()
        {
            // Arrange
            var ctx = _fixture.Build();
            Sut.SendOnce = true;

            // Act
            _fixture.Harness.Respond(HttpStatusCode.Accepted, "queued", "text/plain");
            ctx.AdvanceTime(TimeSpan.Zero);

            // Assert
            Assert.Equal(RequestOutcome.Succeeded, Sut.Outcome);
            Assert.Equal(202, Sut.StatusCode);
            Assert.Equal("Accepted", Sut.ReasonPhrase);
            Assert.Equal("text/plain", Sut.ResponseContentType);
            Assert.Contains(new HeaderRow("Content-Type", "text/plain"), Sut.ResponseHeaders);
            Assert.Equal("queued", Sut.ResponseBodyPreview);
            Assert.False(Sut.ResponseBodyTruncated);
            Assert.Empty(Sut.LastError);
        }

        [Fact]
        public void TruncateBodyLongerThanPreview()
        {
            // Arrange
            var ctx = _fixture.Build();
            Sut.SendOnce = true;

            // Act
            _fixture.Harness.Respond(HttpStatusCode.OK, new string('x', 8192 + 1), "text/plain");
            ctx.AdvanceTime(TimeSpan.Zero);

            // Assert
            Assert.True(Sut.ResponseBodyTruncated);
            Assert.Equal(new string('x', 8192), Sut.ResponseBodyPreview);
        }
    }
}