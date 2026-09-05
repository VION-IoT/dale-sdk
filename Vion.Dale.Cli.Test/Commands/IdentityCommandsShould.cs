using System;
using System.CommandLine;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Output;
using Vion.Dale.Cli.Test.TestHelpers;

namespace Vion.Dale.Cli.Test.Commands
{
    [TestClass]
    public class IdentityCommandsShould
    {
        private StubHttpMessageHandler _handler = null!;

        private TextWriter _originalOut = null!;

        private StringWriter _standardOutput = null!;

        private TemporaryStoreRoot _store = null!;

        [TestInitialize]
        public void Setup()
        {
            _store = new TemporaryStoreRoot();
            _handler = new StubHttpMessageHandler();
            DaleHttpClient.UseTransport(_handler);
            AuthService.UseTransport(new StubHttpMessageHandler());
            _originalOut = Console.Out;
            _standardOutput = new StringWriter();
            Console.SetOut(_standardOutput);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Console.SetOut(_originalOut);
            DaleConsole.JsonMode = false;
            DaleHttpClient.UseTransport(null);
            AuthService.UseTransport(null);
            _store.Dispose();
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-018.1")]
        public async Task RefuseWhoamiWhenNothingStored()
        {
            // Arrange / Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "whoami" }).InvokeAsync();

            // Assert
            Assert.AreEqual(1, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-018.2")]
        public async Task ReportWhoamiAsJsonInJsonMode()
        {
            // Arrange
            DaleConsole.JsonMode = true;
            TokenStore.SaveConfig(new DaleConfig { Environment = "test", ApiBaseUrl = "https://api.test.vion.swiss" });
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "token", ExpiresAt = DateTime.UtcNow.AddHours(2), Environment = "test" });
            _handler.Answer(HttpStatusCode.OK,
                            "{\"user\":{\"email\":\"dev@example.test\"},\"integratorMemberships\":[{\"integratorId\":\"11111111-1111-1111-1111-111111111111\",\"integratorName\":\"ACME\",\"integratorSlug\":\"acme\"}]}");

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "whoami", "--output", "json" }).InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            StringAssert.Contains(_standardOutput.ToString(), "\"email\": \"dev@example.test\"");
            StringAssert.Contains(_standardOutput.ToString(), "ACME (acme)");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-018.2")]
        public async Task ReportWhoamiWithoutIdentityWhenApiCannotBeReached()
        {
            // Arrange
            DaleConsole.JsonMode = true;
            TokenStore.SaveConfig(new DaleConfig { Environment = "test" });
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "token", ExpiresAt = DateTime.UtcNow.AddHours(2), Environment = "test" });
            _handler.Answer(HttpStatusCode.InternalServerError, "{}");

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "whoami", "--output", "json" }).InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            StringAssert.Contains(_standardOutput.ToString(), "\"email\": null");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-018.3")]
        public async Task ReportLogoutWhenNothingWasStored()
        {
            // Arrange
            DaleConsole.JsonMode = true;

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "logout", "--output", "json" }).InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            StringAssert.Contains(_standardOutput.ToString(), "\"cleared\": false");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-018.3")]
        public async Task ReportLogoutWhenCredentialsWereCleared()
        {
            // Arrange
            DaleConsole.JsonMode = true;
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "token", ExpiresAt = DateTime.UtcNow.AddHours(2) });

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "logout", "--output", "json" }).InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            StringAssert.Contains(_standardOutput.ToString(), "\"cleared\": true");
            Assert.IsNull(TokenStore.LoadCredentials());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-018.6")]
        public async Task RefuseClearingIntegratorWithoutConfirmation()
        {
            // Arrange
            DaleConsole.JsonMode = true;
            TokenStore.SaveConfig(new DaleConfig { Environment = "test", IntegratorId = Guid.NewGuid(), IntegratorName = "ACME" });

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "config", "set-environment", "production", "--output", "json" }).InvokeAsync();

            // Assert
            Assert.AreEqual(1, exit);
            Assert.AreEqual("test", TokenStore.LoadConfig().Environment);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-018.6")]
        public async Task SwitchEnvironmentWhenConfirmationWasForced()
        {
            // Arrange
            TokenStore.SaveConfig(new DaleConfig { Environment = "test", IntegratorId = Guid.NewGuid(), IntegratorName = "ACME" });

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "config", "set-environment", "production", "--force" }).InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            var config = TokenStore.LoadConfig();
            Assert.AreEqual("production", config.Environment);
            Assert.IsNull(config.IntegratorId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-018.5")]
        public async Task SelectSingleMembershipWithoutAsking()
        {
            // Arrange
            var only = Guid.Parse("66666666-6666-6666-6666-666666666666");
            TokenStore.SaveConfig(new DaleConfig { Environment = "test" });
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "token", ExpiresAt = DateTime.UtcNow.AddHours(2), Environment = "test" });
            _handler.Answer(HttpStatusCode.OK,
                            $"{{\"user\":{{\"email\":\"a@b.test\"}},\"integratorMemberships\":[{{\"integratorId\":\"{only}\",\"integratorName\":\"Only\",\"integratorSlug\":\"only\"}}]}}");

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "config", "set-integrator" }).InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            Assert.AreEqual(only, TokenStore.LoadConfig().IntegratorId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-018.5")]
        public async Task RefuseToChooseBetweenMembershipsInJsonMode()
        {
            // Arrange
            DaleConsole.JsonMode = true;
            TokenStore.SaveConfig(new DaleConfig { Environment = "test" });
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "token", ExpiresAt = DateTime.UtcNow.AddHours(2), Environment = "test" });
            _handler.Answer(HttpStatusCode.OK,
                            "{\"user\":{\"email\":\"a@b.test\"},\"integratorMemberships\":[" +
                            "{\"integratorId\":\"11111111-1111-1111-1111-111111111111\",\"integratorName\":\"First\",\"integratorSlug\":\"first\"}," +
                            "{\"integratorId\":\"22222222-2222-2222-2222-222222222222\",\"integratorName\":\"Second\",\"integratorSlug\":\"second\"}]}");

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "config", "set-integrator", "--output", "json" }).InvokeAsync();

            // Assert
            Assert.AreEqual(1, exit);
            StringAssert.Contains(_standardOutput.ToString(), "--integrator-id");
        }
    }
}