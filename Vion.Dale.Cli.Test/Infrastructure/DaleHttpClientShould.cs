using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Test.TestHelpers;

namespace Vion.Dale.Cli.Test.Infrastructure
{
    [TestClass]
    public class DaleHttpClientShould
    {
        private StubHttpMessageHandler _handler = null!;

        [TestInitialize]
        public void Setup()
        {
            _handler = new StubHttpMessageHandler();
            DaleHttpClient.UseTransport(_handler);
        }

        [TestCleanup]
        public void Cleanup()
        {
            DaleHttpClient.UseTransport(null);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.1")]
        public async Task SendBearerTokenAndUserAgentOnEveryRequest()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.OK, "{}");

            // Act
            await DaleHttpClient.GetAsync("https://api.example.test/me", "the-access-token");

            // Assert
            var request = _handler.Requests.Single();
            Assert.AreEqual("Bearer", request.Headers.Authorization!.Scheme);
            Assert.AreEqual("the-access-token", request.Headers.Authorization.Parameter);
            Assert.AreEqual("Vion.Dale.Cli", request.Headers.UserAgent.Single().Product!.Name);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.5")]
        public async Task ReturnResponseWhenCallerAllowsItsStatus()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.Conflict, "{\"message\":\"version already exists\"}");

            // Act
            var response = await DaleHttpClient.PostAsync("https://api.example.test/upload", new StringContent("payload"), "token", default, HttpStatusCode.Conflict);

            // Assert
            Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.2")]
        [DataRow(HttpStatusCode.Unauthorized, "Session expired. Run `dale login` again.")]
        [DataRow(HttpStatusCode.Forbidden, "Access denied. Check your integrator permissions.")]
        public async Task NameRecoveryForRefusedRequest(HttpStatusCode statusCode, string expectedMessage)
        {
            // Arrange
            _handler.Answer(statusCode, "{}");

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => DaleHttpClient.GetAsync("https://api.example.test/me", "token"));

            // Assert
            Assert.AreEqual(expectedMessage, exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.2")]
        public async Task NameEndpointWhenNotFound()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.NotFound, "{}");

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => DaleHttpClient.GetAsync("https://api.example.test/missing", "token"));

            // Assert
            Assert.AreEqual("Endpoint not found: https://api.example.test/missing", exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.2")]
        public async Task ReportStatusCodeAndServerMessageForAnyOtherFailure()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.InternalServerError, "{\"message\":\"the library store is down\"}");

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => DaleHttpClient.GetAsync("https://api.example.test/me", "token"));

            // Assert
            Assert.AreEqual("API error 500: the library store is down", exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.3")]
        public async Task ReportTimedOutRequestWithItsMethodAndUrl()
        {
            // Arrange
            _handler.AnswerBy((_, _) => throw new TaskCanceledException());

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => DaleHttpClient.GetAsync("https://api.example.test/me", "token"));

            // Assert
            Assert.AreEqual("Request timed out: GET https://api.example.test/me", exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.3")]
        public async Task ReportUnreachableHostWithItsCause()
        {
            // Arrange
            _handler.AnswerBy((_, _) => throw new HttpRequestException("No such host is known."));

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => DaleHttpClient.GetAsync("https://api.example.test/me", "token"));

            // Assert
            Assert.AreEqual("Network error: No such host is known.. Check your connectivity and API URL.", exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.4")]
        public void ShowServerSentenceOutOfErrorEnvelope()
        {
            // Arrange
            var body = "{\"statusCode\":409,\"exceptionType\":\"ConflictException\",\"message\":\"  version already exists  \"}";

            // Act
            var described = DaleHttpClient.DescribeError(body);

            // Assert
            Assert.AreEqual("version already exists", described);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.4")]
        public void ShowServerSentenceWhateverCaseEnvelopeUses()
        {
            // Arrange
            var body = "{\"StatusCode\":500,\"Message\":\"upstream unavailable\"}";

            // Act
            var described = DaleHttpClient.DescribeError(body);

            // Assert
            Assert.AreEqual("upstream unavailable", described);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-017.4")]
        [DataRow("<html><body>502 Bad Gateway</body></html>", "<html><body>502 Bad Gateway</body></html>")]
        [DataRow("{\"statusCode\":500}", "{\"statusCode\":500}")]
        [DataRow("", "(no response body)")]
        [DataRow("   ", "(no response body)")]
        public void FallBackToRawBodyWhenNotEnvelope(string body, string expected)
        {
            // Arrange / Act
            var described = DaleHttpClient.DescribeError(body);

            // Assert
            Assert.AreEqual(expected, described);
        }
    }
}