using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Http.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.Http.LogicBlocks;
using Xunit;

namespace Vion.Examples.Http.Test
{
    /// <summary>
    ///     The bundled simulator, driven over the HTTP test kit's in-memory transport: the route table, the request log and
    ///     the refusals are the SDK server's own, and a request is answered from whatever the block last published.
    /// </summary>
    public sealed class HttpSimServerShould : IDisposable
    {
        public HttpSimServerShould()
        {
            _harness = new FakeHttpServerHarness(_clock);
            _sut = new HttpSimServer(_harness.ServerFactory, LogicBlockTestHelper.CreateLoggerMock().Object);
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        private readonly FakeTimeProvider _clock = new();

        private readonly FakeHttpServerHarness _harness;

        private readonly HttpSimServer _sut;

        [Theory]
        [InlineData("GET", "/api/status", HttpStatusCode.OK, "application/json", "{\"device\":\"dale-http-sim\",\"state\":\"ok\"}")]
        [InlineData("POST", "/api/setpoint", HttpStatusCode.Accepted, "application/json", "{\"accepted\":true}")]
        [InlineData("GET", "/api/fault", HttpStatusCode.ServiceUnavailable, "text/plain", "maintenance")]
        public void ServeConfiguredRoute(string method, string path, HttpStatusCode expectedStatus, string expectedContentType, string expectedBody)
        {
            // Arrange
            _sut.CreateTestContext().Build();

            // Act
            var response = _harness.Client.Send(new HttpMethod(method), path);

            // Assert
            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(expectedContentType, response.ContentType);
            Assert.Equal(expectedBody, response.Body);
        }

        [Theory]
        [InlineData(99)]
        [InlineData(600)]
        public void ReportRouteWithInvalidStatus(int status)
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _sut.Route1.StatusCode = status;

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.StartsWith($"Not served: A final HTTP status code lies from 200 to 599; {status} does not.", _sut.Route1.Status);
            Assert.Equal(HttpStatusCode.NotFound, _harness.Client.Send(HttpMethod.Get, "/api/status").StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, _harness.Client.Send(HttpMethod.Post, "/api/setpoint").StatusCode);
        }

        [Theory]
        [InlineData("api/status")]
        [InlineData("/api/status?x=1")]
        [InlineData("")]
        public void ReportRouteWithInvalidPath(string path)
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _sut.Route1.Path = path;

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.StartsWith("Not served: ", _sut.Route1.Status);
            Assert.Equal(HttpStatusCode.Accepted, _harness.Client.Send(HttpMethod.Post, "/api/setpoint").StatusCode);
        }

        [Fact]
        public void CountDroppedRequestsAsAnswered()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            for (var i = 0; i < 300; i++)
            {
                _harness.Client.Send(HttpMethod.Get, "/api/status");
            }

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal(300, _sut.ServerSummary.AnsweredCount);
            Assert.True(_sut.ServerSummary.DroppedCount > 0);
            Assert.Equal(300 - _sut.ServerSummary.DroppedCount, _sut.Route1.HitCount);
        }

        [Fact]
        public void CountHitsPerRoute()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _harness.Client.Send(HttpMethod.Get, "/api/status");
            _clock.Advance(TimeSpan.FromSeconds(3));
            _harness.Client.Send(HttpMethod.Get, "/api/status?verbose=1");

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal(2, _sut.Route1.HitCount);
            Assert.Equal(_clock.GetUtcNow().UtcDateTime, _sut.Route1.LastHitAt);
            Assert.Equal(0, _sut.Route2.HitCount);
            Assert.Equal(2, _sut.ServerSummary.AnsweredCount);
            Assert.Equal(0, _sut.ServerSummary.UnmatchedCount);
        }

        [Fact]
        public void CountUnmatchedRequests()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _harness.Client.Send(HttpMethod.Get, "/nowhere");
            _harness.Client.Send(HttpMethod.Delete, "/api/status");

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal(2, _sut.ServerSummary.UnmatchedCount);
            Assert.Equal(0, _sut.ServerSummary.AnsweredCount);
            Assert.Equal(0, _sut.Route1.HitCount);
        }

        [Fact]
        public void CreditNoRouteForRequestAnsweredBeforeRouteServed()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _sut.Route1.Enabled = false;
            _sut.FireTimer(block => block.OnTick());
            _harness.Client.Send(HttpMethod.Get, "/api/status");
            _sut.Route1.Enabled = true;

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal(0, _sut.Route1.HitCount);
            Assert.Null(_sut.Route1.LastHitAt);
        }

        [Fact]
        public void CreditRouteThatAnswers404()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _sut.Route1.StatusCode = 404;
            _sut.FireTimer(block => block.OnTick());
            _harness.Client.Send(HttpMethod.Get, "/api/status");

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal(1, _sut.Route1.HitCount);
            Assert.Equal(1, _sut.ServerSummary.AnsweredCount);
        }

        [Fact]
        public void KeepRecentRequestsNewestFirst()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            for (var i = 0; i < 10; i++)
            {
                _harness.Client.Send(HttpMethod.Get, $"/first-tick/{i}");
            }

            _sut.FireTimer(block => block.OnTick());
            _harness.Client.Send(HttpMethod.Post, "/api/setpoint", "{}");

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal(10, _sut.RecentRequests.Length);
            Assert.Equal(new[] { "/api/setpoint", "/first-tick/9", "/first-tick/8" }, _sut.RecentRequests.Take(3).Select(row => row.Target));
            Assert.Equal("/first-tick/1", _sut.RecentRequests.Last().Target);
            Assert.Equal(2, _sut.RecentRequests[0].BodyBytes);
        }

        [Fact]
        public void ListenOnceStarted()
        {
            // Arrange

            // Act
            _sut.CreateTestContext().Build();

            // Assert
            Assert.True(_sut.IsListening);
            Assert.Empty(_sut.LastError);
        }

        [Fact]
        public void PublishLastRequestArrival()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _clock.Advance(TimeSpan.FromSeconds(7));
            var arrival = _clock.GetUtcNow().UtcDateTime;
            _harness.Client.Send(HttpMethod.Get, "/api/status");
            _clock.Advance(TimeSpan.FromSeconds(2));

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal(arrival, _sut.LastRequestAt);
            Assert.Equal(arrival, _sut.ServerSummary.LastRequestAt);
        }

        [Fact]
        public void ReportInvalidListenAddress()
        {
            // Arrange
            _sut.CreateTestContext().Build();

            // Act
            _sut.ListenAddress = "192.168.1.x";

            // Assert
            Assert.Contains("192.168.1.x", _sut.LastError);
            Assert.False(_sut.IsListening);
        }

        [Fact]
        public void ReportRouteWithInvalidContentType()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _sut.Route1.ContentType = "text/plain\r\nX-Injected: 1";

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.StartsWith("Not served: ", _sut.Route1.Status);
            Assert.Contains("printable ASCII", _sut.Route1.Status);
            Assert.Equal(HttpStatusCode.NotFound, _harness.Client.Send(HttpMethod.Get, "/api/status").StatusCode);
        }

        [Fact]
        public void ServeEditedRouteAfterTick()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _sut.Route1.StatusCode = 500;
            _sut.Route1.Body = "{\"state\":\"broken\"}";
            var beforeTick = _harness.Client.Send(HttpMethod.Get, "/api/status");

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            var afterTick = _harness.Client.Send(HttpMethod.Get, "/api/status");
            Assert.Equal(HttpStatusCode.OK, beforeTick.StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, afterTick.StatusCode);
            Assert.Equal("{\"state\":\"broken\"}", afterTick.Body);
            Assert.Equal("Serving GET /api/status → 500.", _sut.Route1.Status);
        }

        [Fact]
        public void ServeFirstOfDuplicateRoutes()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _sut.Route3.StatusCode = 418;
            _sut.Route3.Path = "/api/status";

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal(HttpStatusCode.OK, _harness.Client.Send(HttpMethod.Get, "/api/status").StatusCode);
            Assert.Equal("Not served: an earlier slot already answers GET /api/status.", _sut.Route3.Status);
        }

        [Fact]
        public void ServeNoSlotAboveCount()
        {
            // Arrange
            var sut = new HttpSimServer(_harness.ServerFactory, LogicBlockTestHelper.CreateLoggerMock().Object) { RouteSlotCount = 1 };

            // Act
            sut.CreateTestContext().Build();

            // Assert
            Assert.Equal(HttpStatusCode.OK, _harness.Client.Send(HttpMethod.Get, "/api/status").StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, _harness.Client.Send(HttpMethod.Post, "/api/setpoint").StatusCode);
        }

        [Fact]
        public void ServeNothingForDisabledSlot()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _sut.Route1.Enabled = false;

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, _harness.Client.Send(HttpMethod.Get, "/api/status").StatusCode);
            Assert.Equal("Disabled.", _sut.Route1.Status);
        }

        [Fact]
        public void ShowLastRequestInFull()
        {
            // Arrange
            _sut.CreateTestContext().Build();
            _harness.Client.Send(HttpMethod.Get, "/api/status");
            _harness.Client.Send(HttpMethod.Post, "/api/setpoint?unit=kW", "{\"value\":42}", new Dictionary<string, string> { ["Content-Type"] = "application/json" });

            // Act
            _sut.FireTimer(block => block.OnTick());

            // Assert
            Assert.Equal("POST /api/setpoint?unit=kW", _sut.LastRequestLine);
            Assert.Equal(new HeaderRow("Content-Type", "application/json"), Assert.Single(_sut.LastRequestHeaders));
            Assert.Equal("{\"value\":42}", _sut.LastRequestBody);
        }

        [Fact]
        public void StopListeningWhenDisabled()
        {
            // Arrange
            _sut.CreateTestContext().Build();

            // Act
            _sut.ServerEnabled = false;

            // Assert
            Assert.False(_sut.IsListening);
            Assert.Throws<InvalidOperationException>(() => _harness.Client.Send(HttpMethod.Get, "/api/status"));
        }
    }
}