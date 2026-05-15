using System.Collections.Generic;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Models;

namespace Vion.Dale.Cli.Test.Models
{
    [TestClass]
    public class CliListOutputTests
    {
        [TestMethod]
        public void Serialization_UsesCamelCase()
        {
            var output = new CliListOutput
                         {
                             PackageId = "Test.Package",
                             Version = "1.0.0",
                             SdkVersion = "0.1.60",
                         };

            var json = JsonSerializer.Serialize(output, JsonDefaults.Options);

            Assert.IsTrue(json.Contains("\"packageId\""));
            Assert.IsTrue(json.Contains("\"sdkVersion\""));
        }

        [TestMethod]
        public void RoundTrip_PreservesData()
        {
            var output = new CliListOutput
                         {
                             PackageId = "Test.Package",
                             Version = "1.0.0",
                             SdkVersion = "0.1.60",
                             LogicBlocks = new List<CliLogicBlockOutput>
                                           {
                                               new()
                                               {
                                                   Name = "MyBlock",
                                                   FullName = "Test.MyBlock",
                                                   Interfaces = new List<string> { "ITemperature" },
                                                   Contracts = new List<string> { "AO1" },
                                               },
                                           },
                         };

            var json = JsonSerializer.Serialize(output, JsonDefaults.Options);
            var deserialized = JsonSerializer.Deserialize<CliListOutput>(json, JsonDefaults.Options);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual("Test.Package", deserialized.PackageId);
            Assert.AreEqual(1, deserialized.LogicBlocks.Count);
            Assert.AreEqual("MyBlock", deserialized.LogicBlocks[0].Name);
            Assert.AreEqual(1, deserialized.LogicBlocks[0].Interfaces.Count);
        }
    }
}
