using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;
using Vion.Dale.Cli.Commands;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Models;

namespace Vion.Dale.Cli.Test.Commands
{
    [TestClass]
    public class ListCommandShould
    {
        private static readonly DaleProject Project = new()
                                                      {
                                                          ProjectName = "MyLib",
                                                          PackageId = "MyLib",
                                                          Version = "1.0.0",
                                                          SdkVersion = "0.11.2",
                                                      };

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.4")]
        [DataRow("MyLib.Blocks.Thermostat", "Thermostat")]
        [DataRow("MyLib.Blocks.Outer+Inner", "Inner")]
        [DataRow("MyLib.Blocks.Outer+Middle+Inner", "Inner")]
        [DataRow("Thermostat", "Thermostat")]
        [DataRow(null, "Unknown")]
        [DataRow("", "Unknown")]
        public void RenderLastSegmentOfIdentityAsShortName(string? typeFullName, string expectedShortName)
        {
            // Arrange / Act
            var shortName = ListCommand.ShortName(typeFullName);

            // Assert
            Assert.AreEqual(expectedShortName, shortName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.5")]
        public void ReportEveryBindingWhateverItsIdentifier()
        {
            // Arrange
            var info = new DalePluginInfo
                       {
                           LogicBlocks = new List<LogicBlockResult>
                                         {
                                             new()
                                             {
                                                 TypeFullName = "MyLib.Thermostat",
                                                 Contracts = new List<ContractInfo>
                                                             {
                                                                 new() { Identifier = "Heater" },
                                                                 new() { Identifier = string.Empty },
                                                             },
                                                 Interfaces = new List<InterfaceInfo>
                                                              {
                                                                  new() { Identifier = string.Empty },
                                                                  new() { Identifier = "Peer" },
                                                              },
                                             },
                                         },
                       };

            // Act
            var output = ListCommand.MapToCliOutput(info, Project);

            // Assert
            CollectionAssert.AreEqual(new[] { "Heater", string.Empty }, output.LogicBlocks.Single().Contracts.ToArray());
            CollectionAssert.AreEqual(new[] { string.Empty, "Peer" }, output.LogicBlocks.Single().Interfaces.ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.6")]
        public void ReportBlockWhoseDocumentCarriesNoIdentity()
        {
            // Arrange
            var info = new DalePluginInfo
                       {
                           LogicBlocks = new List<LogicBlockResult>
                                         {
                                             new()
                                             {
                                                 TypeFullName = null!,
                                                 Contracts = null!,
                                                 Interfaces = null!,
                                                 Services = null!,
                                             },
                                         },
                       };

            // Act
            var output = ListCommand.MapToCliOutput(info, Project);

            // Assert
            Assert.AreEqual("Unknown", output.LogicBlocks.Single().Name);
            Assert.AreEqual(0, output.LogicBlocks.Single().Contracts.Count);
            Assert.AreEqual(0, output.LogicBlocks.Single().Services.Count);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.6")]
        public void RenderBlockWhoseDocumentCarriesNoIdentity()
        {
            // Arrange
            var info = new DalePluginInfo
                       {
                           LogicBlocks = new List<LogicBlockResult>
                                         {
                                             new() { TypeFullName = null!, Contracts = null!, Interfaces = null!, Services = null! },
                                         },
                       };
            var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
                                             {
                                                 Ansi = AnsiSupport.No,
                                                 ColorSystem = ColorSystemSupport.NoColors,
                                                 Out = new AnsiConsoleOutput(writer),
                                             });

            // Act
            ListCommand.RenderTable(console, Project, info);

            // Assert
            StringAssert.Contains(writer.ToString(), "Unknown");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.7")]
        public void CarryPackageIdentityAndVersionsFromDocumentBeforeProject()
        {
            // Arrange
            var info = new DalePluginInfo { PackageId = "Renamed.Lib", PackageVersion = "2.0.0" };

            // Act
            var output = ListCommand.MapToCliOutput(info, Project);

            // Assert
            Assert.AreEqual("Renamed.Lib", output.PackageId);
            Assert.AreEqual("2.0.0", output.Version);
            Assert.AreEqual("0.11.2", output.SdkVersion);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.7")]
        public void FallBackToProjectIdentityWhenDocumentCarriesNone()
        {
            // Arrange
            var info = new DalePluginInfo { PackageId = null!, PackageVersion = null! };

            // Act
            var output = ListCommand.MapToCliOutput(info, Project);

            // Assert
            Assert.AreEqual("MyLib", output.PackageId);
            Assert.AreEqual("1.0.0", output.Version);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.3")]
        public void MarkBlockBoundToDevelopmentOnlyContract()
        {
            // Arrange
            // The parser serializes annotation values as JSON, so the mirror hands the CLI a JsonElement —
            // the shape `dale list` actually reads, not an in-process bool.
            var info = PluginInfoWithContract(JsonSerializer.Deserialize<Dictionary<string, object>>("""{ "developmentOnly": true }""")!);

            // Act
            var output = ListCommand.MapToCliOutput(info, Project);

            // Assert
            Assert.IsTrue(output.LogicBlocks.Single().DevelopmentOnly);
            CollectionAssert.Contains(output.LogicBlocks.Single().Contracts, "LightChannel");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.3")]
        public void LeaveOrdinaryBlockUnmarked()
        {
            // Arrange
            var info = PluginInfoWithContract(new Dictionary<string, object>());

            // Act
            var output = ListCommand.MapToCliOutput(info, Project);

            // Assert
            Assert.IsFalse(output.LogicBlocks.Single().DevelopmentOnly);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void DerivePropertyAndMeasuringPointTypeFromSchema()
        {
            // Arrange
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

            // Act
            var output = ListCommand.MapToCliOutput(PluginInfoWithSingleService(service), Project);

            // Assert
            var mappedService = output.LogicBlocks.Single().Services.Single();
            Assert.AreEqual("VoltageSetpoint", mappedService.Properties[0].Name);
            Assert.AreEqual("double", mappedService.Properties[0].Type);
            Assert.AreEqual("ScheduledSetpoint?", mappedService.Properties[1].Type);
            Assert.AreEqual("CurrentAlarm", mappedService.MeasuringPoints[0].Name);
            Assert.AreEqual("AlarmState", mappedService.MeasuringPoints[0].Type);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        public void ReportEmptyTypeWhenDocumentCarriesNoSchema()
        {
            // Arrange
            var service = new ServiceInfo { Identifier = "Root", Properties = new List<ServicePropertyInfo> { new() { Identifier = "LegacyProperty" } } };

            // Act
            var output = ListCommand.MapToCliOutput(PluginInfoWithSingleService(service), Project);

            // Assert
            var mappedProperty = output.LogicBlocks.Single().Services.Single().Properties[0];
            Assert.AreEqual("LegacyProperty", mappedProperty.Name);
            Assert.AreEqual(string.Empty, mappedProperty.Type);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.7")]
        public void CarryServiceNameAndGatePredicate()
        {
            // Arrange
            var gated = new ServiceInfo { Identifier = "chargePoint2", IncludedWhen = "ChargePointCount >= 2" };

            // Act
            var output = ListCommand.MapToCliOutput(PluginInfoWithSingleService(gated), Project);

            // Assert
            var mappedService = output.LogicBlocks.Single().Services.Single();
            Assert.AreEqual("chargePoint2", mappedService.Name);
            Assert.AreEqual("ChargePointCount >= 2", mappedService.IncludedWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.7")]
        public void CarryNoGatePredicateForUngatedService()
        {
            // Arrange
            var service = new ServiceInfo { Identifier = "Root" };

            // Act
            var output = ListCommand.MapToCliOutput(PluginInfoWithSingleService(service), Project);

            // Assert
            Assert.IsNull(output.LogicBlocks.Single().Services.Single().IncludedWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.9")]
        public void RenderIdentifierCarryingMarkupCharactersLiterally()
        {
            // Arrange
            var info = new DalePluginInfo
                       {
                           LogicBlocks = new List<LogicBlockResult>
                                         {
                                             new()
                                             {
                                                 TypeFullName = "MyLib.Relay",
                                                 Contracts = new List<ContractInfo>
                                                             {
                                                                 new()
                                                                 {
                                                                     Identifier = "Channel[0]",
                                                                     MatchingContractType = "DigitalOutputProvider",
                                                                 },
                                                             },
                                                 Interfaces = new List<InterfaceInfo> { new() { Identifier = "Peer[red]" } },
                                             },
                                         },
                       };
            var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
                                             {
                                                 Ansi = AnsiSupport.No,
                                                 ColorSystem = ColorSystemSupport.NoColors,
                                                 Out = new AnsiConsoleOutput(writer),
                                             });

            // Act
            ListCommand.RenderTable(console, Project, info);

            // Assert
            var rendered = writer.ToString();
            StringAssert.Contains(rendered, "Channel[0]");
            StringAssert.Contains(rendered, "Peer[red]");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.11")]
        public void RenderProjectHeadingAndSayWhenProjectDeclaresNoLogicBlock()
        {
            // Arrange
            var info = new DalePluginInfo { LogicBlocks = new List<LogicBlockResult>() };
            var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
                                             {
                                                 Ansi = AnsiSupport.No,
                                                 ColorSystem = ColorSystemSupport.NoColors,
                                                 Out = new AnsiConsoleOutput(writer),
                                             });

            // Act
            ListCommand.RenderTable(console, Project, info);

            // Assert
            var rendered = writer.ToString();
            StringAssert.Contains(rendered, "Project: MyLib");
            StringAssert.Contains(rendered, "No logic blocks found.");
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
                                         new() { TypeFullName = "Test.Blocks.MyBlock", Services = new List<ServiceInfo> { service } },
                                     },
                   };
        }
    }
}