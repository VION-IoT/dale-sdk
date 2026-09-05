using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Test.TestHelpers;

namespace Vion.Dale.Cli.Test.Auth
{
    [TestClass]
    public class TokenProviderShould
    {
        private const string TokenResponse = "{\"access_token\":\"minted-token\",\"expires_in\":300,\"refresh_token\":\"minted-refresh\"}";

        private StubHttpMessageHandler _handler = null!;

        private TemporaryStoreRoot _store = null!;

        [TestInitialize]
        public void Setup()
        {
            _store = new TemporaryStoreRoot();
            _handler = new StubHttpMessageHandler();
            AuthService.UseTransport(_handler);
            Environment.SetEnvironmentVariable("DALE_CLIENT_ID", null);
            Environment.SetEnvironmentVariable("DALE_CLIENT_SECRET", null);
        }

        [TestCleanup]
        public void Cleanup()
        {
            AuthService.UseTransport(null);
            Environment.SetEnvironmentVariable("DALE_CLIENT_ID", null);
            Environment.SetEnvironmentVariable("DALE_CLIENT_SECRET", null);
            _store.Dispose();
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.6")]
        public async Task RefuseStoredLoginMintedForAnotherEnvironment()
        {
            // Arrange
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "test-token", ExpiresAt = DateTime.UtcNow.AddHours(1), Environment = "test" });

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => TokenProvider.GetAccessTokenAsync(environment: "production"));

            // Assert
            StringAssert.Contains(exception.Message, "'test'");
            StringAssert.Contains(exception.Message, "'production'");
            StringAssert.Contains(exception.Message, "dale login -e production");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.6")]
        public async Task UseStoredLoginMintedForRequestedEnvironment()
        {
            // Arrange
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "test-token", ExpiresAt = DateTime.UtcNow.AddHours(1), Environment = "TEST" });

            // Act
            var token = await TokenProvider.GetAccessTokenAsync(environment: "test");

            // Assert
            Assert.AreEqual("test-token", token);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.2")]
        public async Task TakeCredentialsFromFlagsBeforeAnythingElse()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DALE_CLIENT_ID", "env-client");
            Environment.SetEnvironmentVariable("DALE_CLIENT_SECRET", "env-secret");
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "stored-token", ExpiresAt = DateTime.UtcNow.AddHours(1), Environment = "test" });
            _handler.Answer(HttpStatusCode.OK, TokenResponse);

            // Act
            var token = await TokenProvider.GetAccessTokenAsync("flag-client", "flag-secret", "test");

            // Assert
            Assert.AreEqual("minted-token", token);
            StringAssert.Contains(await _handler.Requests.Single().Content!.ReadAsStringAsync(), "client_id=flag-client");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.2")]
        public async Task TakeCredentialsFromEnvironmentWhenNoFlagsGiven()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DALE_CLIENT_ID", "env-client");
            Environment.SetEnvironmentVariable("DALE_CLIENT_SECRET", "env-secret");
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "stored-token", ExpiresAt = DateTime.UtcNow.AddHours(1), Environment = "test" });
            _handler.Answer(HttpStatusCode.OK, TokenResponse);

            // Act
            var token = await TokenProvider.GetAccessTokenAsync(environment: "test");

            // Assert
            Assert.AreEqual("minted-token", token);
            StringAssert.Contains(await _handler.Requests.Single().Content!.ReadAsStringAsync(), "client_id=env-client");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.2")]
        [DataRow("client-only", null)]
        [DataRow(null, "secret-only")]
        public async Task FallThroughToStoredLoginWhenOnlyHalfCredentialPairGiven(string? clientId, string? clientSecret)
        {
            // Arrange
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "stored-token", ExpiresAt = DateTime.UtcNow.AddHours(1), Environment = "test" });

            // Act
            var token = await TokenProvider.GetAccessTokenAsync(clientId, clientSecret, "test");

            // Assert
            Assert.AreEqual("stored-token", token);
            Assert.AreEqual(0, _handler.Requests.Count);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.3")]
        public async Task RefuseNamingEveryWayToSupplyCredentialsWhenNoneStored()
        {
            // Arrange / Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => TokenProvider.GetAccessTokenAsync(environment: "test"));

            // Assert
            StringAssert.Contains(exception.Message, "dale login");
            StringAssert.Contains(exception.Message, "DALE_CLIENT_ID");
            StringAssert.Contains(exception.Message, "--client-id");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.4")]
        public async Task RefreshExpiredTokenAndStoreResult()
        {
            // Arrange
            TokenStore.SaveCredentials(new StoredCredentials
                                       { AccessToken = "stale", RefreshToken = "the-refresh", ExpiresAt = DateTime.UtcNow.AddSeconds(-1), Environment = "test" });
            _handler.Answer(HttpStatusCode.OK, TokenResponse);

            // Act
            var token = await TokenProvider.GetAccessTokenAsync(environment: "test");

            // Assert
            Assert.AreEqual("minted-token", token);
            Assert.AreEqual("minted-token", TokenStore.LoadCredentials()!.AccessToken);
            StringAssert.Contains(await _handler.Requests.Single().Content!.ReadAsStringAsync(), "grant_type=refresh_token");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.5")]
        public async Task RefuseWhenRefreshFails()
        {
            // Arrange
            TokenStore.SaveCredentials(new StoredCredentials
                                       { AccessToken = "stale", RefreshToken = "the-refresh", ExpiresAt = DateTime.UtcNow.AddSeconds(-1), Environment = "test" });
            _handler.Answer(HttpStatusCode.BadRequest, "{}");

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => TokenProvider.GetAccessTokenAsync(environment: "test"));

            // Assert
            Assert.AreEqual("Stored token expired and refresh failed. Please run `dale login` again.", exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.5")]
        public async Task RefuseWhenExpiredWithNoRefreshToken()
        {
            // Arrange
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "stale", ExpiresAt = DateTime.UtcNow.AddSeconds(-1), Environment = "test" });

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => TokenProvider.GetAccessTokenAsync(environment: "test"));

            // Assert
            Assert.AreEqual("Stored token expired with no refresh token. Please run `dale login` again.", exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.5")]
        public async Task ExchangeAgainstNamedEnvironmentRealm()
        {
            // Arrange
            _handler.Answer(HttpStatusCode.OK, TokenResponse);

            // Act
            await TokenProvider.GetAccessTokenAsync("client", "secret", "test");

            // Assert
            Assert.AreEqual("https://auth.test.vion.swiss/realms/vion/protocol/openid-connect/token", _handler.Requests.Single().RequestUri!.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.5")]
        public async Task ExchangeAgainstCustomEnvironmentUrlFromStoredConfiguration()
        {
            // Arrange
            TokenStore.SaveConfig(new DaleConfig
                                  {
                                      Environment = "staging",
                                      AuthBaseUrl = "https://auth.staging.example.test/realms/vion",
                                      ApiBaseUrl = "https://api.staging.example.test",
                                  });
            _handler.Answer(HttpStatusCode.OK, TokenResponse);

            // Act
            await TokenProvider.GetAccessTokenAsync("client", "secret");

            // Assert
            Assert.AreEqual("https://auth.staging.example.test/realms/vion/protocol/openid-connect/token", _handler.Requests.Single().RequestUri!.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.5")]
        public async Task RefuseWhenEnvironmentResolvesToNoAuthUrl()
        {
            // Arrange
            TokenStore.SaveConfig(new DaleConfig { Environment = "staging" });

            // Act
            var exception = await Assert.ThrowsExactlyAsync<DaleAuthException>(() => TokenProvider.GetAccessTokenAsync("client", "secret"));

            // Assert
            Assert.AreEqual("Cannot resolve auth URL for environment 'staging'. Run `dale login` or `dale config set-environment` first.", exception.Message);
            Assert.AreEqual(0, _handler.Requests.Count);
        }
    }
}