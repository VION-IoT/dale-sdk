using System.Text.Json;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.Cli.Test.Infrastructure
{
    [TestClass]
    public class JsonDefaultsTests
    {
        [TestMethod]
        public void Options_UsesCamelCase()
        {
            var config = new DaleConfig { Environment = "test", AuthBaseUrl = "https://example.com" };
            var json = JsonSerializer.Serialize(config, JsonDefaults.Options);

            Assert.IsTrue(json.Contains("\"environment\""));
            Assert.IsTrue(json.Contains("\"authBaseUrl\""));
            Assert.IsFalse(json.Contains("\"Environment\""));
            Assert.IsFalse(json.Contains("\"AuthBaseUrl\""));
        }

        [TestMethod]
        public void Options_IsCaseInsensitiveOnRead()
        {
            var json = @"{ ""Environment"": ""staging"", ""AuthBaseUrl"": ""https://test.com"" }";
            var config = JsonSerializer.Deserialize<DaleConfig>(json, JsonDefaults.Options);

            Assert.IsNotNull(config);
            Assert.AreEqual("staging", config.Environment);
            Assert.AreEqual("https://test.com", config.AuthBaseUrl);
        }

        [TestMethod]
        public void Options_IsIndented()
        {
            var config = new DaleConfig { Environment = "test" };
            var json = JsonSerializer.Serialize(config, JsonDefaults.Options);

            Assert.IsTrue(json.Contains("\n"));
        }
    }
}
