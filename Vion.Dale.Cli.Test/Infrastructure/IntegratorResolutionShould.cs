using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Test.TestHelpers;

namespace Vion.Dale.Cli.Test.Infrastructure
{
    [TestClass]
    public class IntegratorResolutionShould
    {
        private StubHttpMessageHandler _handler = null!;

        private TemporaryStoreRoot _store = null!;

        [TestInitialize]
        public void Setup()
        {
            _store = new TemporaryStoreRoot();
            _handler = new StubHttpMessageHandler();
            DaleHttpClient.UseTransport(_handler);
            AuthService.UseTransport(new StubHttpMessageHandler());
            Environment.SetEnvironmentVariable("DALE_INTEGRATOR_ID", null);
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "token", ExpiresAt = DateTime.UtcNow.AddHours(1), Environment = "test" });
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable("DALE_INTEGRATOR_ID", null);
            DaleHttpClient.UseTransport(null);
            AuthService.UseTransport(null);
            _store.Dispose();
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.1")]
        public async Task TakeIntegratorFromFlagBeforeAnythingElse()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DALE_INTEGRATOR_ID", "22222222-2222-2222-2222-222222222222");
            var flagged = Guid.Parse("11111111-1111-1111-1111-111111111111");

            // Act
            var context = await CommandContext.ResolveAsync("test", flagged);

            // Assert
            Assert.AreEqual(flagged, context.IntegratorId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.1")]
        public async Task TakeIntegratorFromEnvironmentWhenNoFlagGiven()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DALE_INTEGRATOR_ID", "22222222-2222-2222-2222-222222222222");

            // Act
            var context = await CommandContext.ResolveAsync("test");

            // Assert
            Assert.AreEqual(Guid.Parse("22222222-2222-2222-2222-222222222222"), context.IntegratorId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.2")]
        public async Task RefuseMalformedIntegratorEnvironmentVariable()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DALE_INTEGRATOR_ID", "1111-not-a-guid");

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => CommandContext.ResolveAsync("test"));

            // Assert
            StringAssert.Contains(exception.Message, "DALE_INTEGRATOR_ID");
            StringAssert.Contains(exception.Message, "1111-not-a-guid");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.1")]
        public async Task TakeIntegratorFromStoredConfigurationWhenNoFlagOrVariableGiven()
        {
            // Arrange
            var stored = Guid.Parse("33333333-3333-3333-3333-333333333333");
            TokenStore.SaveConfig(new DaleConfig { Environment = "test", IntegratorId = stored, IntegratorName = "Stored" });

            // Act
            var context = await CommandContext.ResolveAsync("test");

            // Assert
            Assert.AreEqual(stored, context.IntegratorId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.3")]
        [TestProperty("spec", "AC-CLI-011.10")]
        public async Task TakeSingleMembershipFromApiWhenNothingElseNamesOne()
        {
            // Arrange
            _handler.Answer(System.Net.HttpStatusCode.OK,
                            "{\"user\":{\"email\":\"a@b.test\"},\"integratorMemberships\":[{\"integratorId\":\"44444444-4444-4444-4444-444444444444\",\"integratorName\":\"Only\",\"integratorSlug\":\"only\"}]}");

            // Act
            var context = await CommandContext.ResolveAsync("test");

            // Assert
            Assert.AreEqual(Guid.Parse("44444444-4444-4444-4444-444444444444"), context.IntegratorId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.3")]
        public async Task RefuseWhenApiReportsNoMemberships()
        {
            // Arrange
            _handler.Answer(System.Net.HttpStatusCode.OK, "{\"user\":{\"email\":\"a@b.test\"},\"integratorMemberships\":[]}");

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => CommandContext.ResolveAsync("test"));

            // Assert
            StringAssert.Contains(exception.Message, "No integrator memberships");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-014.3")]
        public async Task RefuseListingMembershipsWhenApiReportsSeveral()
        {
            // Arrange
            _handler.Answer(System.Net.HttpStatusCode.OK,
                            "{\"user\":{\"email\":\"a@b.test\"},\"integratorMemberships\":[" +
                            "{\"integratorId\":\"44444444-4444-4444-4444-444444444444\",\"integratorName\":\"First\",\"integratorSlug\":\"first\"}," +
                            "{\"integratorId\":\"55555555-5555-5555-5555-555555555555\",\"integratorName\":\"Second\",\"integratorSlug\":\"second\"}]}");

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => CommandContext.ResolveAsync("test"));

            // Assert
            StringAssert.Contains(exception.Message, "--integrator-id");
            StringAssert.Contains(exception.Message, "First");
            StringAssert.Contains(exception.Message, "Second");
        }
    }
}