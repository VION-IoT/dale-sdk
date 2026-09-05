using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Test.TestHelpers;

namespace Vion.Dale.Cli.Test.Auth
{
    [TestClass]
    public class TokenStoreShould
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
        [TestProperty("spec", "AC-CLI-015.1")]
        public void KeepCredentialsAndConfigurationInDaleDirectoryUnderUserProfile()
        {
            // Arrange
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "token" });
            TokenStore.SaveConfig(new DaleConfig { Environment = "test" });

            // Act
            var written = Directory.GetFiles(Path.Combine(_store.Root, ".dale"));

            // Assert
            CollectionAssert.AreEquivalent(new[] { _store.CredentialsPath, _store.ConfigPath }, written);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-015.1")]
        public void CreateStoreDirectoryOnFirstWrite()
        {
            // Arrange
            Assert.IsFalse(Directory.Exists(Path.Combine(_store.Root, ".dale")));

            // Act
            TokenStore.SaveConfig(new DaleConfig());

            // Assert
            Assert.IsTrue(Directory.Exists(Path.Combine(_store.Root, ".dale")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-015.2")]
        public void RestrictCredentialsToOwnerOffWindows()
        {
            // Arrange
            if (OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("Unix file modes do not exist on Windows; TokenStore.SetFilePermissions guards on the same predicate.");
                return;
            }

            // Act
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "token" });

            // Assert
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_store.CredentialsPath));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-015.3")]
        public void ReportNoCredentialsWhenNoneStored()
        {
            // Arrange / Act
            var credentials = TokenStore.LoadCredentials();

            // Assert
            Assert.IsNull(credentials);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-015.3")]
        public void ReportNoCredentialsWhenStoredFileUnreadable()
        {
            // Arrange
            _store.WriteCredentials("{ this is not json");

            // Act
            var credentials = TokenStore.LoadCredentials();

            // Assert
            Assert.IsNull(credentials);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-015.4")]
        public void ReportDefaultConfigurationWhenNoneStored()
        {
            // Arrange / Act
            var config = TokenStore.LoadConfig();

            // Assert
            Assert.AreEqual("production", config.Environment);
            Assert.IsNull(config.IntegratorId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-015.4")]
        [DataRow("{ this is not json")]
        [DataRow("null")]
        public void ReportDefaultConfigurationWhenStoredFileUnreadable(string stored)
        {
            // Arrange
            _store.WriteConfig(stored);

            // Act
            var config = TokenStore.LoadConfig();

            // Assert
            Assert.AreEqual("production", config.Environment);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-015.5")]
        public void RoundTripStoredCredentials()
        {
            // Arrange
            var expiresAt = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            // Act
            TokenStore.SaveCredentials(new StoredCredentials
                                       {
                                           AccessToken = "access",
                                           RefreshToken = "refresh",
                                           ExpiresAt = expiresAt,
                                           Environment = "test",
                                       });
            var reloaded = TokenStore.LoadCredentials();

            // Assert
            Assert.AreEqual("access", reloaded!.AccessToken);
            Assert.AreEqual("refresh", reloaded.RefreshToken);
            Assert.AreEqual(expiresAt, reloaded.ExpiresAt);
            Assert.AreEqual("test", reloaded.Environment);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-015.6")]
        public void RemoveStoredCredentialsOnDeletion()
        {
            // Arrange
            TokenStore.SaveCredentials(new StoredCredentials { AccessToken = "token" });
            TokenStore.SaveConfig(new DaleConfig { Environment = "test" });

            // Act
            TokenStore.DeleteCredentials();

            // Assert
            Assert.IsFalse(File.Exists(_store.CredentialsPath));
            Assert.IsTrue(File.Exists(_store.ConfigPath));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-015.6")]
        public void CompleteDeletionWhenNothingStored()
        {
            // Arrange / Act / Assert
            TokenStore.DeleteCredentials();
            Assert.IsFalse(File.Exists(_store.CredentialsPath));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-012.1")]
        [DataRow(-1, true)]
        [DataRow(29, true)]
        [DataRow(31, false)]
        [DataRow(3600, false)]
        public void TreatTokenAsExpiredThirtySecondsAhead(int secondsUntilExpiry, bool expectedExpired)
        {
            // Arrange
            var credentials = new StoredCredentials { ExpiresAt = DateTime.UtcNow.AddSeconds(secondsUntilExpiry) };

            // Act
            var expired = credentials.IsExpired;

            // Assert
            Assert.AreEqual(expectedExpired, expired);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.1")]
        [DataRow("production", "https://auth.vion.swiss/realms/vion", "https://api.vion.swiss")]
        [DataRow("test", "https://auth.test.vion.swiss/realms/vion", "https://api.test.vion.swiss")]
        [DataRow("PRODUCTION", "https://auth.vion.swiss/realms/vion", "https://api.vion.swiss")]
        [DataRow("Test", "https://auth.test.vion.swiss/realms/vion", "https://api.test.vion.swiss")]
        public void ResolveNamedEnvironmentUrlsCaseInsensitively(string environment, string expectedAuthUrl, string expectedApiUrl)
        {
            // Arrange / Act
            var authUrl = TokenStore.ResolveAuthBaseUrl(environment);
            var apiUrl = TokenStore.ResolveApiBaseUrl(environment);

            // Assert
            Assert.AreEqual(expectedAuthUrl, authUrl);
            Assert.AreEqual(expectedApiUrl, apiUrl);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.2")]
        public void ReportNoUrlsForEnvironmentItDoesNotKnow()
        {
            // Arrange / Act
            var authUrl = TokenStore.ResolveAuthBaseUrl("staging");
            var apiUrl = TokenStore.ResolveApiBaseUrl("staging");

            // Assert
            Assert.IsNull(authUrl);
            Assert.IsNull(apiUrl);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-013.2")]
        [DataRow("production", true)]
        [DataRow("test", true)]
        [DataRow("PRODUCTION", true)]
        [DataRow("staging", false)]
        [DataRow("", false)]
        public void KnowOnlyProductionAndTestByName(string environment, bool expectedKnown)
        {
            // Arrange / Act
            var known = TokenStore.IsKnownEnvironment(environment);

            // Assert
            Assert.AreEqual(expectedKnown, known);
        }
    }
}
