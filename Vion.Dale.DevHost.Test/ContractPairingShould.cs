using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Topologies;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Contract pairing (RFC 0020): two service-provider contract endpoints declared as ONE wire, so a
    ///     simulator block bound to a provider face closes the loop a real service provider would. The
    ///     structural refusals land at topology load / <c>PairContracts</c>; the wire-type identity rule of
    ///     §4.3 lands when the host loads, where the handler each contract talks to is known.
    /// </summary>
    [TestClass]
    public class ContractPairingShould
    {
        private const string IoBlockType = "Vion.Dale.DevHost.SmokeHost.LogicBlocks.IoBlock";

        private const string IdealIoType = "Vion.Dale.DevHost.SmokeHost.LogicBlocks.IdealIoBlock";

        private const string GridType = "Vion.Dale.DevHost.SmokeHost.LogicBlocks.GridBlock";

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.6")]
        [TestProperty("spec", "AC-SCEN-014.5")]
        public async Task RefusePairingWithNoTypeIdenticalDirectionNamingBothDeclaredTypes()
        {
            // Act / Assert
            // A digital output paired to an unrelated inbound-only contract: nothing either side declares is the
            // same struct, so no direction can materialise. The refusal must name what was compared — the whole
            // point of the identity rule is that the diagnosis is "wrong type", not a field diff.
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("mismatched")
                                                .AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("IoBlock", out var io)
                                                .AddLogicBlock<SmokeHost.LogicBlocks.GridBlock>("GridBlock", out var grid)
                                                .PairContracts(io, "ActiveOutput", grid, "Demand")
                                                .Build();

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            var e = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

            // Assert
            StringAssert.Contains(e.Message, "no type-identical direction");
            StringAssert.Contains(e.Message, "IoBlock.ActiveOutput");
            StringAssert.Contains(e.Message, "GridBlock.Demand");
            StringAssert.Contains(e.Message, nameof(Sdk.DigitalIo.Output.SetDigitalOutput), "the message must name the outbound type it could not place");
            StringAssert.Contains(e.Message, "GridDemandReceived", "the message must name the inbound type it was compared against");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.4")]
        public async Task MaterialiseBothDirectionsOfConsumerProviderPair()
        {
            // Arrange / Act
            // The canonical pair: a digital output and its provider face reuse the SAME wire structs, so the
            // command flows one way and the confirmation the other. Both directions are reported on the exported
            // configuration, which is what the resolver and the wiring view read.
            var config = PairedIoConfiguration();

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            var pairings = host.Control.GetConfiguration().ContractPairings;
            // Assert
            Assert.HasCount(2, pairings);

            var output = pairings.Single(p => p.A.ContractIdentifier == "ActiveOutput");
            Assert.IsTrue(output.AToB, "SetDigitalOutput is the provider face's declared inbound.");
            Assert.IsTrue(output.BToA, "DigitalOutputChanged is the output's declared inbound since VION-131.");

            var input = pairings.Single(p => p.A.ContractIdentifier == "EnableInput");
            Assert.IsFalse(input.AToB, "A digital input writes nothing, so it can feed nothing.");
            Assert.IsTrue(input.BToA, "The input provider's Drive is the only direction of that face.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.12")]
        [TestCategory("Smoke")]
        public async Task CloseOutputConfirmationLoopThroughSimulator()
        {
            // The primitive, end to end and in one place: the block commands its output, the command reaches the
            // provider face as SetReceived, the ideal module confirms, and OutputChanged lands back on the block
            // — all within stepped advancement, so this is deterministic rather than settle-timed.

            // Arrange
            var config = PairedIoConfiguration();

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            // Act
            await host.Control.SetPropertyAsync("IdealIo", "InputClosed", true);
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(3));

            // Assert
            Assert.IsTrue(host.Control.GetProperty("IoBlock", "IsEnabled") as bool?, "the simulator's own logic drove the paired digital input");
            Assert.IsTrue(host.Control.GetProperty("IdealIo", "LastCommand") as bool?, "the block's output command reached the provider face");
            Assert.IsTrue(host.Control.GetProperty("IoBlock", "ConfirmedActive") as bool?, "and the confirmation came back through OutputChanged");
            Assert.IsFalse(host.Control.GetProperty("IoBlock", "ConfirmationMismatch") as bool?, "an ideal module confirms exactly what it was commanded");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.10")]
        public async Task WarnWithoutFailingWhenScenarioDrivesInboundPairingAlsoFeeds()
        {
            // RFC 0020 §4.6: legal (last write wins) and occasionally what an author wants, so the run proceeds —
            // but two writers on one inbound are invisible in the file, so the report says so. The scenario
            // asserts nothing about the VALUE deliberately: which writer lands last is exactly the thing the
            // warning is about, so a test that depended on it would be the flake it warns against.

            // Arrange
            var dir = Path.Combine(Path.GetTempPath(), "dale-pairing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "seed.scenario.json"),
                              """
                              {
                                "version": 1, "id": "seed", "topology": "paired-io",
                                "steps": [
                                  { "serviceProviderSet": { "logicBlock": "IoBlock", "contract": "EnableInput" }, "value": true }
                                ]
                              }
                              """);

            var config = PairedIoConfiguration();
            config.ScenariosPath = dir;

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync("seed", host.Control, dir);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, string.Join("; ", report.ValidationErrors));
            Assert.HasCount(1, report.ValidationWarnings);
            StringAssert.Contains(report.ValidationWarnings[0], "IoBlock.EnableInput");
            StringAssert.Contains(report.ValidationWarnings[0], "IdealIo.InputChannel");
            StringAssert.Contains(report.ValidationWarnings[0], "last write wins");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.3")]
        public void RefusePairingNamingContractItsBlockDoesNotBind()
        {
            // Act / Assert
            var e = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyLoader.Build(Topology("""
                                                                                                      { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "NoSuchContract" },
                                                                                                        "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                                                                                                      """)));

            // Assert
            StringAssert.Contains(e.Message, "has no contract 'NoSuchContract'");
            StringAssert.Contains(e.Message, "ActiveOutput", "the refusal lists what the block does bind");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.2")]
        public void RefusePairingNamingUndeclaredBlock()
        {
            // Act / Assert
            var e = Assert.ThrowsExactly<InvalidDataException>(() => Topology("""
                                                                              { "a": { "logicBlockName": "NoSuchBlock", "contractIdentifier": "ActiveOutput" },
                                                                                "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                                                                              """));

            // Assert
            StringAssert.Contains(e.Message, "'NoSuchBlock' is not a declared instance");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.2")]
        public void RefusePairingWhoseEndpointsCoincide()
        {
            // Act / Assert
            // Self-pairing is the host-synthesised confirmation this design deliberately dropped (§4.5).
            var e = Assert.ThrowsExactly<InvalidDataException>(() => Topology("""
                                                                              { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
                                                                                "b": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" } }
                                                                              """));

            // Assert
            StringAssert.Contains(e.Message, "both endpoints are 'IoBlock.ActiveOutput'");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.3")]
        public void RefuseSamePairDeclaredTwice()
        {
            // Act / Assert
            const string Entry = """
                                 { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
                                   "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                                 """;

            // Declared once each way round — symmetric, so the second is the same wire, not a fan-out.
            var e = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyLoader.Build(Topology(Entry + """
                                                                                                              ,{ "a": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" },
                                                                                                                 "b": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" } }
                                                                                                              """)));

            // Assert
            StringAssert.Contains(e.Message, "are already paired");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.8")]
        [TestProperty("spec", "AC-SCEN-014.1")]
        public void ResolveEachEndpointToItsOwnAutoCreatedEndpointAndSurviveSerializationRoundTrip()
        {
            // Arrange / Act
            var file = Topology("""
                                { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
                                  "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                                """);

            var pairing = DevTopologyLoader.Build(file).ContractPairings.Single();
            // Assert
            Assert.AreEqual("IoBlock", pairing.A.LogicBlockName);
            Assert.AreEqual("ActiveOutput", pairing.A.ContractEndpointIdentifier, "the endpoint a forward addresses is the auto-created one");
            Assert.AreEqual("IdealIo", pairing.B.LogicBlockName);
            Assert.AreNotEqual(pairing.A.ServiceProviderIdentifier, pairing.B.ServiceProviderIdentifier, "pairing needs no shared endpoint triple (VION-133 stays out of it)");

            // Serialize + re-parse: an editor Save must not silently drop a wire.
            var reparsed = DevTopologyFile.Parse(file.ToJson());
            Assert.HasCount(1, reparsed.ContractPairings!);
            Assert.AreEqual("ActiveOutput", reparsed.ContractPairings![0].A!.ContractIdentifier);
            Assert.AreEqual("OutputChannel", reparsed.ContractPairings[0].B!.ContractIdentifier);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.7")]
        public void OmitPairingsFromUnpairedTopologyFile()
        {
            // Arrange / Act
            // The default is preserved byte for byte: a topology with no pairings serializes exactly as before.
            var file = DevTopologyFile.Parse($$"""
                                               { "id": "plain", "logicBlockInstances": [ { "typeFullName": "{{IoBlockType}}", "name": "IoBlock" } ] }
                                               """);

            // Assert
            Assert.IsNull(file.ContractPairings);
            StringAssert.DoesNotMatch(file.ToJson(), new System.Text.RegularExpressions.Regex("contractPairings"));
            Assert.IsEmpty(DevTopologyLoader.Build(file).ContractPairings);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.7")]
        [TestCategory("Smoke")]
        public async Task RefuseMismatchedPairingAtSaveAndValidateNamingBothDeclaredTypes()
        {
            // Arrange / Act
            // The identity rule of §4.3 needs the handler each contract talks to, which only an introspected
            // block carries — so DevTopologyLoader.Build stays structural and a RUNNING host hands its
            // introspection to the topology store. The editor's validate and its save then refuse a
            // type-mismatched pairing where it was authored, instead of at the next host start. The boot-time
            // refusal stays as well (the first test in this class) — Save is not the only way a file reaches a host.
            var directory = Path.Combine(Path.GetTempPath(), "dale-pairing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var port = FreePort();

            try
            {
                var config = DevConfigurationBuilder.Create()
                                                    .WithTopologyName("running")
                                                    .AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("IoBlock")
                                                    .AddLogicBlock<SmokeHost.LogicBlocks.IdealIoBlock>("IdealIo")
                                                    .Build();
                config.TopologiesPath = directory;

                await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithWebUi(port).Build();
                await host.StartAsync();

                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

                // A digital output paired to an unrelated inbound-only contract: nothing either side declares is
                // the same struct, so the pairing can carry nothing. GridBlock is deliberately NOT in the running
                // topology — the check introspects the DRAFT's types, not the ones that happen to be wired.
                var mismatched = TopologyJson("mismatched",
                                              """
                                              { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
                                                "b": { "logicBlockName": "GridBlock", "contractIdentifier": "Demand" } }
                                              """);

                var validate = await client.PostAsync("/api/topologies/validate", new StringContent(mismatched, Encoding.UTF8, "application/json"));
                var validateBody = await validate.Content.ReadAsStringAsync();
            // Assert
                Assert.AreEqual(HttpStatusCode.UnprocessableEntity, validate.StatusCode, validateBody);
                StringAssert.Contains(validateBody, "SetDigitalOutput", "The refusal must name what was compared, not just that the pairing is wrong.");
                StringAssert.Contains(validateBody, "GridDemandReceived");

                var save = await client.PutAsync("/api/topologies/mismatched", new StringContent(mismatched, Encoding.UTF8, "application/json"));
                var saveBody = await save.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.UnprocessableEntity, save.StatusCode, saveBody);
                StringAssert.Contains(saveBody, "no type-identical direction");
                Assert.IsFalse(File.Exists(Path.Combine(directory, "mismatched.topology.json")), "A refused save must not have written the file.");

                // The canonical pair — a consumer face and its provider face — passes the same check.
                var paired = TopologyJson("paired",
                                          """
                                          { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
                                            "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                                          """);
                var ok = await client.PostAsync("/api/topologies/validate", new StringContent(paired, Encoding.UTF8, "application/json"));
                Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode, await ok.Content.ReadAsStringAsync());
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.7")]
        public void SaveUnpairedTopologyByteIdenticallyEvenAfterEditorTouchedItsPairings()
        {
            // Arrange / Act
            // The editor creates contractPairings on the first pair and drops the key with the last, but a draft
            // that reaches the server carrying an EMPTY array must still land as an unpaired file: an author who
            // added and then removed a pairing changed nothing, and the saved bytes have to say so.
            var directory = Path.Combine(Path.GetTempPath(), "dale-pairing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var store = new DevTopologyStore(directory);
                var plain = store.Save("plain", PlainTopologyJson(string.Empty));
                var withoutKey = File.ReadAllText(plain);

                var touched = store.Save("plain", PlainTopologyJson(""", "contractPairings": []"""));

            // Assert
                Assert.AreEqual(withoutKey, File.ReadAllText(touched), "An empty pairing list must save exactly as no pairing list at all.");
                StringAssert.DoesNotMatch(File.ReadAllText(touched), new System.Text.RegularExpressions.Regex("contractPairings"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.1")]
        public void KeepPairedTopologyEntriesThroughUnrelatedEdit()
        {
            // Arrange / Act
            // The round-trip a paired bench depends on: the editor adds a block, saves, and the wires it never
            // touched are still there. (p3 fixed the DRAFT's silent deletion; this pins the server half.)
            var directory = Path.Combine(Path.GetTempPath(), "dale-pairing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var store = new DevTopologyStore(directory);
                var pairing = """
                              { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
                                "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                              """;

                store.Save("bench", TopologyJson("bench", pairing));
                var edited = store.Save("bench", TopologyJson("bench", pairing, true));

                var reparsed = DevTopologyFile.Parse(File.ReadAllText(edited));
            // Assert
                Assert.HasCount(4, reparsed.LogicBlockInstances!, "The unrelated edit added a fourth block.");
                Assert.HasCount(1, reparsed.ContractPairings!, "An unrelated edit must not drop the pairing.");
                Assert.AreEqual("OutputChannel", reparsed.ContractPairings![0].B!.ContractIdentifier);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static int FreePort()
        {
            // OS-assigned free port — avoids fixed-port collisions when this runs alongside the rest of the
            // solution's test assemblies in parallel.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        // The fixture's paired bench, built in C# so PairContracts is exercised alongside the file form.
        private static DevConfiguration PairedIoConfiguration()
        {
            return DevConfigurationBuilder.Create()
                                          .WithTopologyName("paired-io")
                                          .AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("IoBlock", out var io)
                                          .AddLogicBlock<SmokeHost.LogicBlocks.IdealIoBlock>("IdealIo", out var ideal)
                                          .PairContracts(io, "ActiveOutput", ideal, "OutputChannel")
                                          .PairContracts(io, "EnableInput", ideal, "InputChannel")
                                          .Build();
        }

        // The same three-block fixture Topology() parses, as raw JSON — what a topology editor PUTs.
        // extraInstance adds an unrelated fourth block, standing in for "an edit that touched something else".
        private static string TopologyJson(string id, string pairings, bool extraInstance = false)
        {
            var extra = extraInstance ? $$"""{ "typeFullName": "{{IdealIoType}}", "name": "Spare" },""" : string.Empty;
            return $$"""
                     {
                       "id": "{{id}}",
                       "logicBlockInstances": [
                         {{extra}}
                         { "typeFullName": "{{IoBlockType}}", "name": "IoBlock" },
                         { "typeFullName": "{{IdealIoType}}", "name": "IdealIo" },
                         { "typeFullName": "{{GridType}}", "name": "GridBlock" }
                       ],
                       "contractPairings": [ {{pairings}} ]
                     }
                     """;
        }

        // One unpaired block, with an optional extra top-level field — the "the editor touched pairings and
        // put them back" draft.
        private static string PlainTopologyJson(string extraField)
        {
            return $$"""
                     { "id": "plain", "logicBlockInstances": [ { "typeFullName": "{{IoBlockType}}", "name": "IoBlock" } ]{{extraField}} }
                     """;
        }

        private static DevTopologyFile Topology(string pairings)
        {
            return DevTopologyFile.Parse($$"""
                                           {
                                             "id": "paired",
                                             "logicBlockInstances": [
                                               { "typeFullName": "{{IoBlockType}}", "name": "IoBlock" },
                                               { "typeFullName": "{{IdealIoType}}", "name": "IdealIo" },
                                               { "typeFullName": "{{GridType}}", "name": "GridBlock" }
                                             ],
                                             "contractPairings": [ {{pairings}} ]
                                           }
                                           """);
        }
    }
}