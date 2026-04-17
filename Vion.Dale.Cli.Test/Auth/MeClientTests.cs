using System.Text.Json;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.Cli.Test.Auth
{
    [TestClass]
    public class MeClientTests
    {
        [TestMethod]
        public void Deserialize_SingleIntegrator()
        {
            var json = @"{
                ""user"": { ""email"": ""test@example.com"" },
                ""integratorMemberships"": [
                    { ""integratorId"": ""11111111-1111-1111-1111-111111111111"", ""integratorSlug"": ""acme"", ""integratorName"": ""ACME Corp"" }
                ],
                ""tenantMemberships"": [],
                ""platformMemberships"": []
            }";
            var result = JsonSerializer.Deserialize<MeResponse>(json, JsonDefaults.Options);

            Assert.IsNotNull(result);
            Assert.AreEqual("test@example.com", result.User.Email);
            Assert.AreEqual(1, result.IntegratorMemberships.Count);
            Assert.AreEqual("ACME Corp", result.IntegratorMemberships[0].IntegratorName);
            Assert.AreEqual("acme", result.IntegratorMemberships[0].IntegratorSlug);
        }

        [TestMethod]
        public void Deserialize_MultipleIntegrators()
        {
            var json = @"{
                ""user"": { ""email"": ""multi@example.com"" },
                ""integratorMemberships"": [
                    { ""integratorId"": ""11111111-1111-1111-1111-111111111111"", ""integratorSlug"": ""acme"", ""integratorName"": ""ACME"" },
                    { ""integratorId"": ""22222222-2222-2222-2222-222222222222"", ""integratorSlug"": ""vion"", ""integratorName"": ""Vion"" }
                ],
                ""tenantMemberships"": [],
                ""platformMemberships"": []
            }";
            var result = JsonSerializer.Deserialize<MeResponse>(json, JsonDefaults.Options);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.IntegratorMemberships.Count);
        }

        [TestMethod]
        public void Deserialize_NoIntegrators()
        {
            var json = @"{
                ""user"": { ""email"": ""lonely@example.com"" },
                ""integratorMemberships"": [],
                ""tenantMemberships"": [],
                ""platformMemberships"": []
            }";
            var result = JsonSerializer.Deserialize<MeResponse>(json, JsonDefaults.Options);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.IntegratorMemberships.Count);
        }

        [TestMethod]
        public void Deserialize_NullEmail()
        {
            var json = @"{
                ""user"": { ""email"": null },
                ""integratorMemberships"": [],
                ""tenantMemberships"": [],
                ""platformMemberships"": []
            }";
            var result = JsonSerializer.Deserialize<MeResponse>(json, JsonDefaults.Options);

            Assert.IsNotNull(result);
            Assert.IsNull(result.User.Email);
        }
    }
}