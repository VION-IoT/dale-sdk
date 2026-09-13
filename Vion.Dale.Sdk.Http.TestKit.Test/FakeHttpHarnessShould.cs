using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Http.TestKit.Test
{
    /// <summary>
    ///     The client harness driven the way a block author drives it: a block built through the core kit's context, issuing
    ///     requests through the harness's client, with the test answering and the context running the callbacks. Every
    ///     assertion reads what a callback delivered to the block, so a test whose exchange never completed fails on the
    ///     missing delivery rather than passing on silence.
    /// </summary>
    [TestClass]
    public class FakeHttpHarnessShould
    {
        private const string DescriptionUrl = "http://10.20.0.248/app/api/id/3.json";

        private static readonly DateTimeOffset Anchor = new(2026,
                                                            1,
                                                            1,
                                                            0,
                                                            0,
                                                            0,
                                                            TimeSpan.Zero);

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.1")]
        [TestProperty("spec", "AC-TKIT-014.3")]
        public void DeliverNonSuccessStatusAsSdkMapsIt()
        {
            // Arrange
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            var ctx = sut.CreateTestContext().Build();
            sut.FetchDescription(DescriptionUrl);

            // Act
            harness.Respond(HttpStatusCode.NotFound);
            ctx.FlushPendingActions();

            // Assert
            var (url, outcome) = sut.Settled.Single();
            Assert.AreEqual(DescriptionUrl, url);
            var failure = Assert.IsInstanceOfType<HttpRequestException>(outcome);
            Assert.AreEqual("Response status code does not indicate success: 404 (Not Found).", failure.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.1")]
        [TestProperty("spec", "AC-TKIT-014.3")]
        [DataRow("{\"Serial\":\"MTR-0000-4711\",\"Value\":42}", "MTR-0000-4711", 42, DisplayName = "matching property names")]
        [DataRow("{\"serial\":\"MTR-0000-4711\",\"value\":42}", "", 0, DisplayName = "names in another case stay at their defaults")]
        public void DeserializeScriptedBodyWithSdkSerializer(string body, string expectedSerial, int expectedValue)
        {
            // Arrange — the second row is the SDK serializer's own case-sensitive default, which a lenient stand-in would not reproduce
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            var ctx = sut.CreateTestContext().Build();
            sut.FetchDescription(DescriptionUrl);

            // Act
            harness.Respond(body);
            ctx.FlushPendingActions();

            // Assert
            var description = Assert.IsInstanceOfType<DeviceDescription>(sut.Settled.Single().Outcome);
            Assert.AreEqual(expectedSerial, description.Serial);
            Assert.AreEqual(expectedValue, description.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.2")]
        public void RecordRequestAsComposedForWireBeforeMemberReturns()
        {
            // Arrange
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            sut.CreateTestContext().Build();

            // Act
            sut.FetchDescription(DescriptionUrl, TimeSpan.FromSeconds(5));

            // Assert — the User-Agent is the SDK registration's own, which only a real composition puts on the wire
            var request = harness.Requests.Single();
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual(DescriptionUrl, request.Uri.ToString());
            Assert.AreEqual("Vion-DALE (info@vion-iot.com)", request.Headers["User-Agent"]);
            Assert.IsNull(request.Body);
            Assert.AreEqual(TimeSpan.FromSeconds(5), request.Timeout);
            Assert.AreEqual(1, harness.PendingCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.2")]
        public void RecordSerializedBodyAndContentType()
        {
            // Arrange
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            sut.CreateTestContext().Build();

            // Act
            sut.ReportStatus("http://10.20.0.248/app/api/status", new DeviceDescription { Serial = "MTR-0000-4711", Value = 7 });

            // Assert
            var request = harness.Requests.Single();
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("{\"Serial\":\"MTR-0000-4711\",\"Value\":7}", request.Body);
            Assert.AreEqual("application/json", request.ContentType);
            Assert.IsNull(request.Timeout);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.3")]
        public void AnswerOutstandingRequestsOldestFirst()
        {
            // Arrange
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            var ctx = sut.CreateTestContext().Build();
            sut.FetchDescription("http://10.20.0.248/app/api/id/3.json");
            sut.FetchDescription("http://10.20.0.248/app/api/id/5.json");

            // Act
            harness.Respond("{\"Value\":3}");
            harness.Respond("{\"Value\":5}");
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual("http://10.20.0.248/app/api/id/3.json=3, http://10.20.0.248/app/api/id/5.json=5",
                            string.Join(", ", sut.Settled.Select(settled => $"{settled.Url}={((DeviceDescription)settled.Outcome).Value}")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.3")]
        public void DeliverScriptedFailureUnchanged()
        {
            // Arrange
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            var ctx = sut.CreateTestContext().Build();
            sut.FetchDescription(DescriptionUrl);
            var refused = new SocketException((int)SocketError.ConnectionRefused);

            // Act
            harness.Fail(refused);
            ctx.FlushPendingActions();

            // Assert
            Assert.AreSame(refused, sut.Settled.Single().Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.3")]
        public void HandSendRequestCallbackScriptedResponse()
        {
            // Arrange
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            var ctx = sut.CreateTestContext().Build();
            sut.SendRaw(new HttpRequestMessage(HttpMethod.Put, "http://10.20.0.248/app/api/config") { Content = new StringContent("interval=60") });

            // Act
            harness.Respond(HttpStatusCode.Accepted, "stored", "text/plain");
            ctx.FlushPendingActions();

            // Assert
            var response = Assert.IsInstanceOfType<HttpResponseMessage>(sut.Settled.Single().Outcome);
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            Assert.AreEqual("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.AreEqual("stored", response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.4")]
        public void RefuseRespondWhenNoneOutstanding()
        {
            // Arrange — the only request is already answered, so nothing is outstanding although one was issued
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            sut.CreateTestContext().Build();
            sut.FetchDescription(DescriptionUrl);
            harness.Respond("{}");

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Respond("{}"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.4")]
        public void RefuseFailWhenNoneOutstanding()
        {
            // Arrange
            using var harness = new FakeHttpHarness();

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Fail(new HttpRequestException()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.4")]
        public void RefuseFailWithoutException()
        {
            // Arrange
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            sut.CreateTestContext().Build();
            sut.FetchDescription(DescriptionUrl);

            // Act / Assert
            Assert.AreEqual("exception", Assert.ThrowsExactly<ArgumentNullException>(() => harness.Fail(null!)).ParamName);
            Assert.AreEqual(1, harness.PendingCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.5")]
        public void QueueCallbackOnBlockBeforeAnswerReturns()
        {
            // Arrange
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            var ctx = sut.CreateTestContext().Build();
            sut.FetchDescription(DescriptionUrl);

            // Act
            harness.Respond("{\"Value\":42}");
            var settledDuringAnswer = sut.Settled.Count;
            ctx.FlushPendingActions();

            // Assert — nothing ran during the answer, and the one flush that follows found the callback already queued
            Assert.AreEqual(0, settledDuringAnswer);
            Assert.HasCount(1, sut.Settled);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.6")]
        public void KeepEveryRequestInIssueOrderAndCountOutstanding()
        {
            // Arrange
            using var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            sut.CreateTestContext().Build();
            sut.FetchDescription("http://10.20.0.248/app/api/id/3.json");
            sut.FetchDescription("http://10.20.0.248/app/api/id/5.json");

            // Act
            harness.Respond("{}");

            // Assert
            Assert.AreEqual(1, harness.PendingCount);
            Assert.AreEqual("http://10.20.0.248/app/api/id/3.json, http://10.20.0.248/app/api/id/5.json", string.Join(", ", harness.Requests.Select(request => request.Uri)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.7")]
        public void FailHeldRequestWhenTimeoutElapsesOnHarnessClock()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = new FakeHttpHarness(clock);
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();
            sut.FetchDescription(DescriptionUrl, TimeSpan.FromSeconds(5));
            ctx.AdvanceTime(TimeSpan.FromSeconds(4));
            var pendingBeforeBound = harness.PendingCount;

            // Act
            ctx.AdvanceTime(TimeSpan.FromSeconds(1));
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(1, pendingBeforeBound);
            Assert.AreEqual(0, harness.PendingCount);
            Assert.AreEqual("Timed out after 5 seconds", Assert.IsInstanceOfType<TimeoutException>(sut.Settled.Single().Outcome).Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.8")]
        public void HoldRequestPastItsTimeoutWhenNoClockSupplied()
        {
            // Arrange — a harness on the system clock is the synchronisation point: once its identical request has expired,
            // the same bound has elapsed in real time for the harness under test as well
            using var harness = new FakeHttpHarness();
            using var systemClockHarness = new FakeHttpHarness(TimeProvider.System);
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            var systemClockBlock = new SampleHttpBlock(systemClockHarness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            sut.CreateTestContext().Build();
            systemClockBlock.CreateTestContext().Build();

            // Act
            sut.FetchDescription(DescriptionUrl, TimeSpan.FromMilliseconds(1));
            systemClockBlock.FetchDescription(DescriptionUrl, TimeSpan.FromMilliseconds(1));
            var systemClockExpired = SpinWait.SpinUntil(() => systemClockHarness.PendingCount == 0, TimeSpan.FromSeconds(10));

            // Assert
            Assert.IsTrue(systemClockExpired, "The system-clock harness's request never expired, so no real time is known to have passed.");
            Assert.AreEqual(1, harness.PendingCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.8")]
        public void RefuseNullClock()
        {
            // Arrange

            // Act / Assert
            Assert.AreEqual("timeProvider", Assert.ThrowsExactly<ArgumentNullException>(() => new FakeHttpHarness(null!)).ParamName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.9")]
        public void AbandonOutstandingRequestsOnDispose()
        {
            // Arrange
            var harness = new FakeHttpHarness();
            var sut = new SampleHttpBlock(harness.Client, LogicBlockTestHelper.CreateLoggerMock().Object);
            var ctx = sut.CreateTestContext().Build();
            sut.FetchDescription(DescriptionUrl);

            // Act
            harness.Dispose();
            ctx.FlushPendingActions();

            // Assert
            Assert.IsEmpty(sut.Settled);
        }
    }
}
