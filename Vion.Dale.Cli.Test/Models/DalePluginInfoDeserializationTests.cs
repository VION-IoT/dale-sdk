using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Models
{
    /// <summary>
    ///     Pins the CLI's hand-mirrored parser DTO (Models/DalePluginInfo.cs) to the JSON the
    ///     LogicBlockParser actually emits (Vion.Contracts LogicBlockIntrospectionResult shape).
    ///     The fixture below is trimmed from real parser output produced by the SDK 0.10.0 parser
    ///     against the Gating/RichTypes example libraries.
    /// </summary>
    [TestClass]
    public class DalePluginInfoDeserializationTests
    {
        private const string CurrentParserJson = """
                                                 {
                                                   "packageId": "Vion.Examples.Gating",
                                                   "packageVersion": "0.10.0",
                                                   "annotations": {},
                                                   "logicBlocks": [
                                                     {
                                                       "typeFullName": "Vion.Examples.Gating.LogicBlocks.ChargePark",
                                                       "interfaces": [
                                                         {
                                                           "identifier": "chargePoint2",
                                                           "interfaceTypeFullNames": ["Example.IConsumer"],
                                                           "matchingInterfaceTypeFullNames": ["Example.IController"],
                                                           "annotations": { "includedWhen": "ChargePointCount >= 2" }
                                                         }
                                                       ],
                                                       "contracts": [
                                                         {
                                                           "identifier": "light_do",
                                                           "contractTypeFullName": "Example.IDigitalOutput",
                                                           "matchingContractType": "DigitalOutput",
                                                           "annotations": {}
                                                         }
                                                       ],
                                                       "services": [
                                                         {
                                                           "identifier": "chargePoint2",
                                                           "includedWhen": "ChargePointCount >= 2",
                                                           "interfaceTypeFullNames": ["Example.IChargePoint"],
                                                           "properties": [
                                                             {
                                                               "identifier": "ChargePointCount",
                                                               "schema": { "type": "integer", "format": "int32", "minimum": 1, "maximum": 3 },
                                                               "presentation": null,
                                                               "runtime": { "instantiationParameter": true, "default": 1 }
                                                             },
                                                             {
                                                               "identifier": "PreferredSetpoint",
                                                               "schema": {
                                                                 "type": ["object", "null"],
                                                                 "title": "ScheduledSetpoint",
                                                                 "properties": { "at": { "type": "string", "format": "date-time" } }
                                                               },
                                                               "presentation": { "displayName": "Geplanter Sollwert", "group": "configuration", "order": 10 },
                                                               "runtime": null
                                                             }
                                                           ],
                                                           "measuringPoints": [
                                                             {
                                                               "identifier": "CurrentAlarm",
                                                               "schema": {
                                                                 "type": "string",
                                                                 "title": "AlarmState",
                                                                 "enum": ["Ok", "Warning", "Critical"],
                                                                 "readOnly": true,
                                                                 "x-kind": "measurement"
                                                               },
                                                               "presentation": { "displayName": "Aktueller Alarm" },
                                                               "runtime": null
                                                             }
                                                           ],
                                                           "inwardRelations": [],
                                                           "outwardRelations": [
                                                             {
                                                               "relationType": "LightToToggle",
                                                               "interfaceIdentifier": "light",
                                                               "interfaceTypeFullName": "Example.IToggleable",
                                                               "annotations": { "defaultName": "Toggles" }
                                                             }
                                                           ]
                                                         },
                                                         {
                                                           "identifier": "Root",
                                                           "includedWhen": null,
                                                           "interfaceTypeFullNames": [],
                                                           "properties": [],
                                                           "measuringPoints": [],
                                                           "inwardRelations": [],
                                                           "outwardRelations": []
                                                         }
                                                       ],
                                                       "annotations": { "defaultName": "Charge Park" }
                                                     }
                                                   ]
                                                 }
                                                 """;

        [TestMethod]
        public void ParsePluginInfo_CurrentParserShape_ReadsPackageAndBlock()
        {
            var info = ParserRunner.ParsePluginInfo(CurrentParserJson);

            Assert.IsNotNull(info);
            Assert.AreEqual("Vion.Examples.Gating", info.PackageId);
            Assert.AreEqual("0.10.0", info.PackageVersion);
            Assert.AreEqual(1, info.LogicBlocks.Count);
            Assert.AreEqual("Vion.Examples.Gating.LogicBlocks.ChargePark", info.LogicBlocks[0].TypeFullName);
        }

        [TestMethod]
        public void ParsePluginInfo_CurrentParserShape_ReadsServiceIncludedWhen()
        {
            var info = ParserRunner.ParsePluginInfo(CurrentParserJson);

            var services = info!.LogicBlocks[0].Services;
            Assert.AreEqual(2, services.Count);
            Assert.AreEqual("ChargePointCount >= 2", services[0].IncludedWhen);
            Assert.IsNull(services[1].IncludedWhen);
        }

        [TestMethod]
        public void ParsePluginInfo_CurrentParserShape_ReadsPropertySiblingDocs()
        {
            var info = ParserRunner.ParsePluginInfo(CurrentParserJson);

            var properties = info!.LogicBlocks[0].Services[0].Properties;
            Assert.AreEqual(2, properties.Count);

            var countProperty = properties[0];
            Assert.AreEqual("ChargePointCount", countProperty.Identifier);
            Assert.IsNotNull(countProperty.Schema);
            Assert.AreEqual("integer", countProperty.Schema["type"]!.GetValue<string>());
            Assert.IsNull(countProperty.Presentation);
            Assert.IsNotNull(countProperty.Runtime);
            Assert.IsTrue(countProperty.Runtime["instantiationParameter"]!.GetValue<bool>());

            var setpointProperty = properties[1];
            Assert.IsNotNull(setpointProperty.Schema);
            Assert.AreEqual("ScheduledSetpoint", setpointProperty.Schema["title"]!.GetValue<string>());
            Assert.IsNotNull(setpointProperty.Presentation);
            Assert.AreEqual("Geplanter Sollwert", setpointProperty.Presentation["displayName"]!.GetValue<string>());
            Assert.IsNull(setpointProperty.Runtime);
        }

        [TestMethod]
        public void ParsePluginInfo_CurrentParserShape_ReadsMeasuringPointSchema()
        {
            var info = ParserRunner.ParsePluginInfo(CurrentParserJson);

            var measuringPoints = info!.LogicBlocks[0].Services[0].MeasuringPoints;
            Assert.AreEqual(1, measuringPoints.Count);
            Assert.AreEqual("CurrentAlarm", measuringPoints[0].Identifier);
            Assert.IsNotNull(measuringPoints[0].Schema);
            Assert.AreEqual(3, measuringPoints[0].Schema!["enum"]!.AsArray().Count);
            Assert.IsTrue(measuringPoints[0].Schema!["readOnly"]!.GetValue<bool>());
        }

        [TestMethod]
        public void ParsePluginInfo_CurrentParserShape_ReadsRelations()
        {
            var info = ParserRunner.ParsePluginInfo(CurrentParserJson);

            var service = info!.LogicBlocks[0].Services[0];
            Assert.AreEqual(0, service.InwardRelations.Count);
            Assert.AreEqual(1, service.OutwardRelations.Count);
            Assert.AreEqual("LightToToggle", service.OutwardRelations[0].RelationType);
            Assert.AreEqual("light", service.OutwardRelations[0].InterfaceIdentifier);
            Assert.AreEqual("Example.IToggleable", service.OutwardRelations[0].InterfaceTypeFullName);
        }

        [TestMethod]
        public void ParsePluginInfo_LegacyParserShape_StillDeserializesTolerantly()
        {
            // Pre-schema parser output (SDK versions before the schema/presentation/runtime split):
            // properties carried typeFullName/writable/serviceElementType/annotations, services had no
            // includedWhen. Unknown members must be ignored and absent members stay default — the CLI
            // must not crash when a consumer project pins an old SDK.
            const string legacyJson = """
                                      {
                                        "packageId": "Legacy.Package",
                                        "packageVersion": "0.8.0",
                                        "annotations": {},
                                        "logicBlocks": [
                                          {
                                            "typeFullName": "Legacy.Block",
                                            "interfaces": [],
                                            "contracts": [],
                                            "services": [
                                              {
                                                "identifier": "Root",
                                                "interfaceTypeFullNames": [],
                                                "properties": [
                                                  {
                                                    "identifier": "Setpoint",
                                                    "typeFullName": "System.Double",
                                                    "writable": true,
                                                    "serviceElementType": "ServiceProperty",
                                                    "annotations": {}
                                                  }
                                                ],
                                                "measuringPoints": [],
                                                "inwardRelations": [],
                                                "outwardRelations": []
                                              }
                                            ],
                                            "annotations": {}
                                          }
                                        ]
                                      }
                                      """;

            var info = ParserRunner.ParsePluginInfo(legacyJson);

            Assert.IsNotNull(info);
            var service = info.LogicBlocks[0].Services[0];
            Assert.IsNull(service.IncludedWhen);
            Assert.AreEqual(1, service.Properties.Count);
            Assert.AreEqual("Setpoint", service.Properties[0].Identifier);
            Assert.IsNull(service.Properties[0].Schema);
        }
    }
}