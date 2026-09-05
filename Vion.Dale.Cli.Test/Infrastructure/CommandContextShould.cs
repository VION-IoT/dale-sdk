using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Test.TestHelpers;

namespace Vion.Dale.Cli.Test.Infrastructure
{
    [TestClass]
    public class CommandContextShould
    {
        private TemporaryStoreRoot _store = null!;

        [TestInitialize]
        public void Setup()
        {
            _store = new TemporaryStoreRoot();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _store.Dispose();
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.3")]
        [DataRow("production", "https://api.vion.swiss", "https://auth.vion.swiss/realms/vion")]
        [DataRow("test", "https://api.test.vion.swiss", "https://auth.test.vion.swiss/realms/vion")]
        public void TakeEnvironmentFromFlagBeforeAnythingStored(string environment, string expectedApiUrl, string expectedAuthUrl)
        {
            // Arrange
            TokenStore.SaveConfig(new DaleConfig { Environment = "staging", AuthBaseUrl = "https://auth.staging.example.test", ApiBaseUrl = "https://api.staging.example.test" });

            // Act
            var context = CommandContext.ResolveLocal(environment);

            // Assert
            Assert.AreEqual(environment, context.Environment);
            Assert.AreEqual(expectedApiUrl, context.ApiBaseUrl);
            Assert.AreEqual(expectedAuthUrl, context.AuthBaseUrl);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.3")]
        public void TakeEnvironmentFromStoredConfigurationWhenNoFlagGiven()
        {
            // Arrange
            TokenStore.SaveConfig(new DaleConfig { Environment = "test" });

            // Act
            var context = CommandContext.ResolveLocal();

            // Assert
            Assert.AreEqual("test", context.Environment);
            Assert.AreEqual("https://api.test.vion.swiss", context.ApiBaseUrl);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.3")]
        public void FallBackToProductionWhenNothingStored()
        {
            // Arrange / Act
            var context = CommandContext.ResolveLocal();

            // Assert
            Assert.AreEqual("production", context.Environment);
            Assert.AreEqual("https://api.vion.swiss", context.ApiBaseUrl);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.4")]
        public void TakeCustomEnvironmentUrlsFromStoredConfiguration()
        {
            // Arrange
            TokenStore.SaveConfig(new DaleConfig
                                  {
                                      Environment = "staging",
                                      AuthBaseUrl = "https://auth.staging.example.test/realms/vion",
                                      ApiBaseUrl = "https://api.staging.example.test",
                                  });

            // Act
            var context = CommandContext.ResolveLocal();

            // Assert
            Assert.AreEqual("staging", context.Environment);
            Assert.AreEqual("https://api.staging.example.test", context.ApiBaseUrl);
            Assert.AreEqual("https://auth.staging.example.test/realms/vion", context.AuthBaseUrl);
        }
    }
}