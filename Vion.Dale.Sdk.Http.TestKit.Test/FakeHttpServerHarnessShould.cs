using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Http.Server;

namespace Vion.Dale.Sdk.Http.TestKit.Test
{
    /// <summary>
    ///     The server harness driven as a simulator author drives it: the block's side publishes through the harness's server,
    ///     the test acts as the network client through the client view. The answers asserted here come from the SDK's own
    ///     route table, so a response the block never published cannot be returned.
    /// </summary>
    [TestClass]
    public class FakeHttpServerHarnessShould
    {
        private const string Description = "{\"Device\":{\"Serial\":\"SIM-0000-0003\"}}";

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-015.1")]
        [TestProperty("spec", "AC-TKIT-015.2")]
        [DataRow("/app/api/id/3.json", HttpStatusCode.OK, Description, DisplayName = "a published path")]
        [DataRow("/app/api/id/4.json", HttpStatusCode.NotFound, "", DisplayName = "a path nothing is published on")]
        public void AnswerFromResponsesBlockPublished(string path, HttpStatusCode expectedStatus, string expectedBody)
        {
            // Arrange
            using var harness = new FakeHttpServerHarness();
            harness.Server.IsEnabled = true;
            harness.Server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/app/api/id/3.json", HttpServerResponse.Json(Description)));

            // Act
            var response = harness.Client.Send(HttpMethod.Get, path);

            // Assert
            Assert.AreEqual(expectedStatus, response.StatusCode);
            Assert.AreEqual(expectedBody, response.Body);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-015.1")]
        public void HandOutSameServerThroughFactory()
        {
            // Arrange
            using var harness = new FakeHttpServerHarness();

            // Act
            var created = harness.ServerFactory.Create();

            // Assert
            Assert.AreSame(harness.Server, created);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-015.1")]
        public void DisposeServerWithHarness()
        {
            // Arrange
            var harness = new FakeHttpServerHarness();
            harness.Server.IsEnabled = true;

            // Act
            harness.Dispose();

            // Assert
            Assert.IsFalse(harness.Server.IsEnabled);
            Assert.IsFalse(harness.Server.IsListening);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-015.1")]
        public void StampRequestsFromSuppliedClock()
        {
            // Arrange
            var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                3,
                                                                1,
                                                                8,
                                                                30,
                                                                0,
                                                                TimeSpan.Zero));
            using var harness = new FakeHttpServerHarness(clock);
            harness.Server.IsEnabled = true;

            // Act
            harness.Client.Send(HttpMethod.Get, "/status");

            // Assert
            Assert.AreEqual(clock.GetUtcNow(), harness.Server.LastRequestAt);
            Assert.AreEqual(clock.GetUtcNow(), harness.Server.Sync(snapshot => snapshot.TakeReceivedRequests().Single().ReceivedAt));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-015.1")]
        public void RefuseNullClock()
        {
            // Arrange

            // Act / Assert
            Assert.AreEqual("timeProvider", Assert.ThrowsExactly<ArgumentNullException>(() => new FakeHttpServerHarness(null!)).ParamName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-015.2")]
        public void RecordClientViewRequestAsSent()
        {
            // Arrange
            using var harness = new FakeHttpServerHarness();
            harness.Server.IsEnabled = true;

            // Act
            harness.Client.Send(HttpMethod.Post, "/app/api/command?unit=3", "{\"Reset\":true}", new Dictionary<string, string> { ["X-Operator"] = "bench" });

            // Assert
            var request = harness.Server.Sync(snapshot => snapshot.TakeReceivedRequests().Single());
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual("/app/api/command", request.Path);
            Assert.AreEqual("unit=3", request.Query);
            Assert.AreEqual("bench", request.Headers["x-operator"]);
            Assert.AreEqual("{\"Reset\":true}", Encoding.UTF8.GetString(request.Body.ToArray()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-015.2")]
        public void ReturnHeadersServerAdds()
        {
            // Arrange
            using var harness = new FakeHttpServerHarness();
            harness.Server.IsEnabled = true;
            harness.Server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/app/api/id/3.json", HttpServerResponse.Json(Description)));

            // Act
            var response = harness.Client.Send(HttpMethod.Put, "/app/api/id/3.json");

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.AreEqual("GET", response.Headers["Allow"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-015.2")]
        public void ReturnContentTypeBlockPublished()
        {
            // Arrange
            using var harness = new FakeHttpServerHarness();
            harness.Server.IsEnabled = true;
            harness.Server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/app/api/id/3.json", HttpServerResponse.Json(Description)));

            // Act
            var response = harness.Client.Send(HttpMethod.Get, "/app/api/id/3.json");

            // Assert
            Assert.AreEqual("application/json", response.ContentType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-015.3")]
        public void RefuseSendWhileServerNotListening()
        {
            // Arrange — published but never enabled, so the refusal is about listening and not about an empty table
            using var harness = new FakeHttpServerHarness();
            harness.Server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/app/api/id/3.json", HttpServerResponse.Json(Description)));

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Client.Send(HttpMethod.Get, "/app/api/id/3.json"));
        }
    }
}