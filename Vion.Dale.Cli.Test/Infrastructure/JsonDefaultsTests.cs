using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Infrastructure;

namespace Vion.Dale.Cli.Test.Infrastructure
{
    [TestClass]
    public class JsonDefaultsTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.10")]
        public void Options_UsesCamelCase()
        {
            // Arrange / Act
            var config = new DaleConfig { Environment = "test", AuthBaseUrl = "https://example.com" };
            var json = JsonSerializer.Serialize(config, JsonDefaults.Options);

            // Assert
            Assert.IsTrue(json.Contains("\"environment\""));
            Assert.IsTrue(json.Contains("\"authBaseUrl\""));
            Assert.IsFalse(json.Contains("\"Environment\""));
            Assert.IsFalse(json.Contains("\"AuthBaseUrl\""));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.10")]
        public void Options_ReadCaseInsensitively()
        {
            // Arrange / Act
            var json = @"{ ""Environment"": ""custom"", ""AuthBaseUrl"": ""https://test.com"" }";
            var config = JsonSerializer.Deserialize<DaleConfig>(json, JsonDefaults.Options);

            // Assert
            Assert.IsNotNull(config);
            Assert.AreEqual("custom", config.Environment);
            Assert.AreEqual("https://test.com", config.AuthBaseUrl);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.10")]
        public void Options_Indent()
        {
            // Arrange / Act
            var config = new DaleConfig { Environment = "test" };
            var json = JsonSerializer.Serialize(config, JsonDefaults.Options);

            // Assert
            Assert.IsTrue(json.Contains("\n"));
        }
    }
}