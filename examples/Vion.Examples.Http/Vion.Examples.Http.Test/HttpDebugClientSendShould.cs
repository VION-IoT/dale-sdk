using System;
using System.Net;
using System.Net.Http;
using Vion.Examples.Http.LogicBlocks;
using Xunit;

namespace Vion.Examples.Http.Test
{
    /// <summary>
    ///     The request pane: what reaches the wire is read off what the SDK composed, and every refusal is proven by the
    ///     absence of a request as well as by the verdict.
    /// </summary>
    public sealed class HttpDebugClientSendShould : IDisposable
    {
        private readonly DebugClientFixture _fixture = new();

        private HttpDebugClient Sut
        {
            get => _fixture.Sut;
        }

        public void Dispose()
        {
            _fixture.Dispose();
        }

        [Theory]
        [InlineData(RequestMethod.Post, "http://device.local/api/setpoint", "Accept: application/json\nContent-Type: application/json", "{\"value\":42}", "POST", "application/json")]
        [InlineData(RequestMethod.Put, "https://10.0.0.7:8443/config?section=grid", "Content-Type: text/plain", "limit=70", "PUT", "text/plain")]
        public void SendRequestAsTyped(RequestMethod method, string url, string headers, string body, string expectedMethod, string expectedContentType)
        {
            // Arrange
            _fixture.Build();
            Sut.Method = method;
            Sut.Url = url;
            Sut.RequestHeaders = headers;
            Sut.RequestBody = body;

            // Act
            Sut.SendOnce = true;

            // Assert
            var request = Assert.Single(_fixture.Harness.Requests);
            Assert.Equal(expectedMethod, request.Method.Method);
            Assert.Equal(url, request.Uri.OriginalString);
            Assert.Equal(body, request.Body);
            Assert.Equal(expectedContentType, request.ContentType);
        }

        [Fact]
        public void SendTypedRequestHeader()
        {
            // Arrange
            _fixture.Build();
            Sut.RequestHeaders = "Authorization: Bearer abc\r\n\r\nX-Trace:  17 ";

            // Act
            Sut.SendOnce = true;

            // Assert
            var request = Assert.Single(_fixture.Harness.Requests);
            Assert.Equal("Bearer abc", request.Headers["Authorization"]);
            Assert.Equal("17", request.Headers["X-Trace"]);
        }

        [Fact]
        public void SendNoBodyWhenBodyEmpty()
        {
            // Arrange
            _fixture.Build();
            Sut.Method = RequestMethod.Post;
            Sut.RequestBody = string.Empty;

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Null(Assert.Single(_fixture.Harness.Requests).Body);
        }

        [Fact]
        public void PassTimeoutToRequest()
        {
            // Arrange
            _fixture.Build();
            Sut.Timeout = TimeSpan.FromSeconds(3);

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(_fixture.Harness.Requests).Timeout);
            Assert.Equal(RequestOutcome.InFlight, Sut.Outcome);
        }

        [Theory]
        [InlineData("/api/status")]
        [InlineData("ftp://device.local/file")]
        [InlineData("http//missing-colon")]
        public void RefuseUrlThatIsNotAbsoluteHttp(string url)
        {
            // Arrange
            _fixture.Build();
            Sut.Url = url;

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Empty(_fixture.Harness.Requests);
            Assert.Equal(RequestOutcome.Invalid, Sut.Outcome);
            Assert.Equal($"'{url}' is not an absolute http:// or https:// URL.", Sut.LastError);
        }

        [Fact]
        public void RefuseMalformedHeaderLine()
        {
            // Arrange
            _fixture.Build();
            Sut.RequestHeaders = "Accept application/json";

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Empty(_fixture.Harness.Requests);
            Assert.Equal(RequestOutcome.Invalid, Sut.Outcome);
            Assert.Equal("The header line 'Accept application/json' is not in the form 'Name: value'.", Sut.LastError);
        }

        [Fact]
        public void RefuseBodyHeaderWithoutBody()
        {
            // Arrange
            _fixture.Build();
            Sut.RequestHeaders = "Content-Type: application/json";
            Sut.RequestBody = string.Empty;

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Empty(_fixture.Harness.Requests);
            Assert.Equal(RequestOutcome.Invalid, Sut.Outcome);
            Assert.Equal("'Content-Type' describes a body, and this request has none. Enter a body to send it.", Sut.LastError);
        }

        [Fact]
        public void RefuseTimeoutNotAboveZero()
        {
            // Arrange
            _fixture.Build();
            Sut.Timeout = TimeSpan.Zero;

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Empty(_fixture.Harness.Requests);
            Assert.Equal(RequestOutcome.Invalid, Sut.Outcome);
            Assert.Equal("The timeout must be longer than zero.", Sut.LastError);
        }

        [Fact]
        public void ClearPreviousResponseWhenRequestInvalid()
        {
            // Arrange
            var ctx = _fixture.Build();
            Sut.SendOnce = true;
            _fixture.Harness.Respond(HttpStatusCode.OK, "{}");
            ctx.AdvanceTime(TimeSpan.Zero);
            var statusBefore = Sut.StatusCode;
            Sut.Url = "not a url";

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Equal(200, statusBefore);
            Assert.Null(Sut.StatusCode);
            Assert.Empty(Sut.ResponseBodyPreview);
        }

        [Fact]
        public void RefuseSendWhileRequestInFlight()
        {
            // Arrange
            _fixture.Build();
            Sut.SendOnce = true;

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Single(_fixture.Harness.Requests);
            Assert.Equal(RequestOutcome.InFlight, Sut.Outcome);
            Assert.Equal("A request is already in flight. Wait for its answer or its timeout before sending again.", Sut.LastError);
        }

        [Fact]
        public void AcceptSendAfterPreviousAnswered()
        {
            // Arrange
            var ctx = _fixture.Build();
            Sut.SendOnce = true;
            _fixture.Harness.Respond(HttpStatusCode.OK, "{}");
            ctx.AdvanceTime(TimeSpan.Zero);

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Equal(2, _fixture.Harness.Requests.Count);
            Assert.Equal(RequestOutcome.InFlight, Sut.Outcome);
        }

        [Fact]
        public void DescribeLastRequest()
        {
            // Arrange
            _fixture.Build();
            Sut.Method = RequestMethod.Post;
            Sut.Url = "http://device.local/api";
            Sut.RequestHeaders = "Accept: */*\nContent-Type: text/plain";
            Sut.RequestBody = "äb";

            // Act
            Sut.SendOnce = true;

            // Assert
            Assert.Equal("POST http://device.local/api — 2 header(s), 3 byte body", Sut.LastRequest);
            Assert.Equal(HttpMethod.Post, Assert.Single(_fixture.Harness.Requests).Method);
        }
    }
}
