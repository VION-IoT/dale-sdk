using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Models;

namespace Vion.Dale.Cli.Test.Commands
{
    /// <summary>
    ///     `dale list -o json` mapping from the parser DTO to the CLI output document.
    /// </summary>
    [TestClass]
    public class ListCommandMappingTests
    {
        [TestMethod]
        public void MapToCliOutput_DerivesPropertyTypeFromSchema()
        {
            var service = new ServiceInfo
                          {
                              Identifier = "Root",
                              Properties = new List<ServicePropertyInfo>
                                           {
                                               new()
                                               {
                                                   Identifier = "VoltageSetpoint",
                                                   Schema = JsonNode.Parse("""{ "type": "number", "format": "double" }"""),
                                               },
                                               new()
                                               {
                                                   Identifier = "PreferredSetpoint",
                                                   Schema =
                                                       JsonNode
                                                           .Parse("""{ "type": ["object", "null"], "title": "ScheduledSetpoint" }"""),
                                               },
                                           },
                              MeasuringPoints = new List<ServiceMeasuringPointInfo>
                                                {
                                                    new()
                                                    {
                                                        Identifier = "CurrentAlarm",
                                                        Schema =
                                                            JsonNode
                                                                .Parse("""{ "type": "string", "title": "AlarmState", "enum": ["Ok"] }"""),
                                                    },
                                                },
                          };

            var output = ListCommand.MapToCliOutput(PluginInfoWithSingleService(service), TestProject());

            var mappedService = output.LogicBlocks[0].Services[0];
            Assert.AreEqual("VoltageSetpoint", mappedService.Properties[0].Name);
            Assert.AreEqual("double", mappedService.Properties[0].Type);
            Assert.AreEqual("ScheduledSetpoint?", mappedService.Properties[1].Type);
            Assert.AreEqual("CurrentAlarm", mappedService.MeasuringPoints[0].Name);
            Assert.AreEqual("AlarmState", mappedService.MeasuringPoints[0].Type);
        }

        [TestMethod]
        public void MapToCliOutput_MissingSchema_MapsEmptyType()
        {
            var service = new ServiceInfo
                          {
                              Identifier = "Root",
                              Properties = new List<ServicePropertyInfo>
                                           {
                                               new() { Identifier = "LegacyProperty" },
                                           },
                          };

            var output = ListCommand.MapToCliOutput(PluginInfoWithSingleService(service), TestProject());

            Assert.AreEqual("LegacyProperty", output.LogicBlocks[0].Services[0].Properties[0].Name);
            Assert.AreEqual(string.Empty, output.LogicBlocks[0].Services[0].Properties[0].Type);
        }

        [TestMethod]
        public void MapToCliOutput_CarriesServiceNameAndIncludedWhen()
        {
            var gated = new ServiceInfo
                        {
                            Identifier = "chargePoint2",
                            IncludedWhen = "ChargePointCount >= 2",
                        };

            var output = ListCommand.MapToCliOutput(PluginInfoWithSingleService(gated), TestProject());

            var mappedService = output.LogicBlocks[0].Services[0];
            Assert.AreEqual("chargePoint2", mappedService.Name);
            Assert.AreEqual("ChargePointCount >= 2", mappedService.IncludedWhen);
        }

        [TestMethod]
        public void MapToCliOutput_UngatedService_HasNullIncludedWhen()
        {
            var service = new ServiceInfo { Identifier = "Root" };

            var output = ListCommand.MapToCliOutput(PluginInfoWithSingleService(service), TestProject());

            Assert.IsNull(output.LogicBlocks[0].Services[0].IncludedWhen);
        }

        [TestMethod]
        public void MapToCliOutput_MarksABlockBoundToADevelopmentOnlyContract()
        {
            // The parser serializes annotation values as JSON, so the mirror hands the CLI a JsonElement —
            // the shape `dale list` actually reads, not an in-process bool.
            var output = ListCommand.MapToCliOutput(PluginInfoWithContract(DevelopmentOnlyAnnotations()), TestProject());

            Assert.IsTrue(output.LogicBlocks[0].DevelopmentOnly);
            CollectionAssert.Contains(output.LogicBlocks[0].Contracts, "LightChannel");
        }

        [TestMethod]
        public void MapToCliOutput_LeavesAnOrdinaryBlockUnmarked()
        {
            var output = ListCommand.MapToCliOutput(PluginInfoWithContract(new Dictionary<string, object>()), TestProject());

            Assert.IsFalse(output.LogicBlocks[0].DevelopmentOnly);
        }

        private static Dictionary<string, object> DevelopmentOnlyAnnotations()
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>("""{ "developmentOnly": true }""")!;
        }

        private static DaleProject TestProject()
        {
            return new DaleProject
                   {
                       CsprojPath = @"C:\tmp\Test.csproj",
                       ProjectName = "Test",
                       ProjectDirectory = @"C:\tmp",
                       SdkVersion = "0.10.0",
                   };
        }

        private static DalePluginInfo PluginInfoWithContract(Dictionary<string, object> contractAnnotations)
        {
            return new DalePluginInfo
                   {
                       PackageId = "Test.Package",
                       PackageVersion = "1.0.0",
                       LogicBlocks = new List<LogicBlockResult>
                                     {
                                         new()
                                         {
                                             TypeFullName = "Test.Blocks.IdealRelay",
                                             Contracts = new List<ContractInfo>
                                                         {
                                                             new()
                                                             {
                                                                 Identifier = "LightChannel",
                                                                 MatchingContractType = "DigitalOutputProvider",
                                                                 Annotations = contractAnnotations,
                                                             },
                                                         },
                                         },
                                     },
                   };
        }

        private static DalePluginInfo PluginInfoWithSingleService(ServiceInfo service)
        {
            return new DalePluginInfo
                   {
                       PackageId = "Test.Package",
                       PackageVersion = "1.0.0",
                       LogicBlocks = new List<LogicBlockResult>
                                     {
                                         new()
                                         {
                                             TypeFullName = "Test.Blocks.MyBlock",
                                             Services = new List<ServiceInfo> { service },
                                         },
                                     },
                   };
        }
    }
}