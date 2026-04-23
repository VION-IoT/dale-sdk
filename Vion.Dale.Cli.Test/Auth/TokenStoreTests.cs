using System;
using Vion.Dale.Cli.Auth;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.Cli.Test.Auth
{
    [TestClass]
    public class TokenStoreTests
    {
        [TestMethod]
        public void StoredCredentials_IsExpired_WhenPastExpiresAt()
        {
            var creds = new StoredCredentials
                        {
                            AccessToken = "test",
                            ExpiresAt = DateTime.UtcNow.AddSeconds(-60),
                        };

            Assert.IsTrue(creds.IsExpired);
        }

        [TestMethod]
        public void StoredCredentials_IsExpired_WithinBuffer()
        {
            var creds = new StoredCredentials
                        {
                            AccessToken = "test",
                            ExpiresAt = DateTime.UtcNow.AddSeconds(10), // Within 30s buffer
                        };

            Assert.IsTrue(creds.IsExpired);
        }

        [TestMethod]
        public void StoredCredentials_NotExpired_WhenFarFuture()
        {
            var creds = new StoredCredentials
                        {
                            AccessToken = "test",
                            ExpiresAt = DateTime.UtcNow.AddHours(1),
                        };

            Assert.IsFalse(creds.IsExpired);
        }

        [TestMethod]
        public void ResolveAuthBaseUrl_Production()
        {
            var url = TokenStore.ResolveAuthBaseUrl("production");
            Assert.AreEqual("https://auth.vion.swiss/realms/vion", url);
        }

        [TestMethod]
        public void ResolveAuthBaseUrl_Test()
        {
            var url = TokenStore.ResolveAuthBaseUrl("test");
            Assert.AreEqual("https://auth.test.vion.swiss/realms/vion", url);
        }

        [TestMethod]
        public void ResolveAuthBaseUrl_UnknownEnvironment_ReturnsNull()
        {
            var url = TokenStore.ResolveAuthBaseUrl("foobar");
            Assert.IsNull(url);
        }

        [TestMethod]
        public void ResolveApiBaseUrl_Test()
        {
            var url = TokenStore.ResolveApiBaseUrl("test");
            Assert.AreEqual("https://api.test.vion.swiss", url);
        }

        [TestMethod]
        public void ResolveApiBaseUrl_Production()
        {
            var url = TokenStore.ResolveApiBaseUrl("production");
            Assert.AreEqual("https://api.vion.swiss", url);
        }

        [TestMethod]
        public void ResolveApiBaseUrl_UnknownEnvironment_ReturnsNull()
        {
            var url = TokenStore.ResolveApiBaseUrl("foobar");
            Assert.IsNull(url);
        }

        [TestMethod]
        public void IsKnownEnvironment_ReturnsTrue_ForKnown()
        {
            Assert.IsTrue(TokenStore.IsKnownEnvironment("test"));
            Assert.IsTrue(TokenStore.IsKnownEnvironment("production"));
        }

        [TestMethod]
        public void IsKnownEnvironment_ReturnsFalse_ForCustom()
        {
            Assert.IsFalse(TokenStore.IsKnownEnvironment("foobar"));
            Assert.IsFalse(TokenStore.IsKnownEnvironment("myenv"));
        }
    }
}