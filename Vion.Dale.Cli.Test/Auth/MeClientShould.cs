using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Test.TestHelpers;

namespace Vion.Dale.Cli.Test.Auth
{
    [TestClass]
    public class MeClientShould
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
        [TestProperty("spec", "AC-CLI-014.4")]
        public async Task AskApiForMembershipsOfCurrentToken()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.OK, "{\"user\":{\"email\":\"a@example.test\"},\"integratorMemberships\":[]}");

            // Act
            await MeClient.GetMeAsync("https://api.test.vion.swiss", "the-token");

            // Assert
            var request = _handler.Requests.Single();
            Assert.AreEqual("https://api.test.vion.swiss/me", request.RequestUri!.ToString());
            Assert.AreEqual("the-token", request.Headers.Authorization!.Parameter);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.5")]
        public async Task ReadMembershipsFromResponse()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.OK,
                            """
                            {
                              "user": { "email": "dev@example.test" },
                              "integratorMemberships": [
                                { "integratorId": "11111111-1111-1111-1111-111111111111", "integratorSlug": "acme", "integratorName": "ACME Corp" },
                                { "integratorId": "22222222-2222-2222-2222-222222222222", "integratorSlug": "vion", "integratorName": "Vion" }
                              ],
                              "tenantMemberships": [],
                              "platformMemberships": []
                            }
                            """);

            // Act
            var me = await MeClient.GetMeAsync("https://api.test.vion.swiss", "token");

            // Assert
            Assert.AreEqual("dev@example.test", me.User.Email);
            CollectionAssert.AreEqual(new[] { "ACME Corp", "Vion" }, me.IntegratorMemberships.Select(m => m.IntegratorName).ToArray());
            CollectionAssert.AreEqual(new[] { "acme", "vion" }, me.IntegratorMemberships.Select(m => m.IntegratorSlug).ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.5")]
        public async Task ReadResponseThatOmitsMembershipsOrEmail()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.OK, "{\"user\":{\"email\":null}}");

            // Act
            var me = await MeClient.GetMeAsync("https://api.test.vion.swiss", "token");

            // Assert
            Assert.IsNull(me.User.Email);
            Assert.AreEqual(0, me.IntegratorMemberships.Count);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.5")]
        public async Task ReadResponseCarryingMembersItDoesNotKnow()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.OK,
                            "{\"user\":{\"email\":\"dev@example.test\",\"displayName\":\"Dev\"},\"someFutureField\":42," +
                            "\"integratorMemberships\":[{\"integratorId\":\"11111111-1111-1111-1111-111111111111\",\"integratorName\":\"ACME\",\"integratorSlug\":\"acme\",\"role\":\"owner\"}]}");

            // Act
            var me = await MeClient.GetMeAsync("https://api.test.vion.swiss", "token");

            // Assert
            Assert.AreEqual("dev@example.test", me.User.Email);
            Assert.AreEqual("ACME", me.IntegratorMemberships.Single().IntegratorName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.5")]
        public async Task RefuseResponseWithoutMembershipDocument()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.OK, "null");

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => MeClient.GetMeAsync("https://api.test.vion.swiss", "token"));

            // Assert
            Assert.AreEqual("Failed to parse /me response.", exception.Message);
        }
    }
}
