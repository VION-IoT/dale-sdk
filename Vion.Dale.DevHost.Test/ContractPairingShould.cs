using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Topologies;

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
        public async Task RefuseAPairingWithNoTypeIdenticalDirectionNamingBothDeclaredTypes()
        {
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

            StringAssert.Contains(e.Message, "no type-identical direction");
            StringAssert.Contains(e.Message, "IoBlock.ActiveOutput");
            StringAssert.Contains(e.Message, "GridBlock.Demand");
            StringAssert.Contains(e.Message, nameof(Sdk.DigitalIo.Output.SetDigitalOutput), "the message must name the outbound type it could not place");
            StringAssert.Contains(e.Message, "GridDemandReceived", "the message must name the inbound type it was compared against");
        }

        [TestMethod]
        public async Task MaterialiseBothDirectionsOfAConsumerProviderPair()
        {
            // The canonical pair: a digital output and its provider face reuse the SAME wire structs, so the
            // command flows one way and the confirmation the other. Both directions are reported on the exported
            // configuration, which is what the resolver and the wiring view read.
            var config = PairedIoConfiguration();

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            var pairings = host.Control.GetConfiguration().ContractPairings;
            Assert.HasCount(2, pairings);

            var output = pairings.Single(p => p.A.ContractIdentifier == "ActiveOutput");
            Assert.IsTrue(output.AToB, "SetDigitalOutput is the provider face's declared inbound.");
            Assert.IsTrue(output.BToA, "DigitalOutputChanged is the output's declared inbound since VION-131.");

            var input = pairings.Single(p => p.A.ContractIdentifier == "EnableInput");
            Assert.IsFalse(input.AToB, "A digital input writes nothing, so it can feed nothing.");
            Assert.IsTrue(input.BToA, "The input provider's Drive is the only direction of that face.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task CloseTheOutputConfirmationLoopThroughTheSimulator()
        {
            // The primitive, end to end and in one place: the block commands its output, the command reaches the
            // provider face as SetReceived, the ideal module confirms, and OutputChanged lands back on the block
            // — all within stepped advancement, so this is deterministic rather than settle-timed.
            var config = PairedIoConfiguration();

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            await host.Control.SetPropertyAsync("IdealIo", "InputClosed", true);
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(3));

            Assert.IsTrue(host.Control.GetProperty("IoBlock", "IsEnabled") as bool?, "the simulator's own logic drove the paired digital input");
            Assert.IsTrue(host.Control.GetProperty("IdealIo", "LastCommand") as bool?, "the block's output command reached the provider face");
            Assert.IsTrue(host.Control.GetProperty("IoBlock", "ConfirmedActive") as bool?, "and the confirmation came back through OutputChanged");
            Assert.IsFalse(host.Control.GetProperty("IoBlock", "ConfirmationMismatch") as bool?, "an ideal module confirms exactly what it was commanded");
        }

        [TestMethod]
        public async Task WarnButNotFailWhenAScenarioDrivesAnInboundAPairingAlsoFeeds()
        {
            // RFC 0020 §4.6: legal (last write wins) and occasionally what an author wants, so the run proceeds —
            // but two writers on one inbound are invisible in the file, so the report says so. The scenario
            // asserts nothing about the VALUE deliberately: which writer lands last is exactly the thing the
            // warning is about, so a test that depended on it would be the flake it warns against.
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

            var report = await ScenarioRunner.RunAsync("seed", host.Control, dir);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, string.Join("; ", report.ValidationErrors));
            Assert.HasCount(1, report.ValidationWarnings);
            StringAssert.Contains(report.ValidationWarnings[0], "IoBlock.EnableInput");
            StringAssert.Contains(report.ValidationWarnings[0], "IdealIo.InputChannel");
            StringAssert.Contains(report.ValidationWarnings[0], "last write wins");
        }

        [TestMethod]
        public void RefuseAPairingNamingAContractTheBlockDoesNotBind()
        {
            var e = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyLoader.Build(Topology("""
                                                                                                      { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "NoSuchContract" },
                                                                                                        "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                                                                                                      """)));

            StringAssert.Contains(e.Message, "has no contract 'NoSuchContract'");
            StringAssert.Contains(e.Message, "ActiveOutput", "the refusal lists what the block does bind");
        }

        [TestMethod]
        public void RefuseAPairingNamingAnUndeclaredBlock()
        {
            var e = Assert.ThrowsExactly<InvalidDataException>(() => Topology("""
                                                                              { "a": { "logicBlockName": "NoSuchBlock", "contractIdentifier": "ActiveOutput" },
                                                                                "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                                                                              """));

            StringAssert.Contains(e.Message, "'NoSuchBlock' is not a declared instance");
        }

        [TestMethod]
        public void RefuseAPairingWhoseEndpointsCoincide()
        {
            // Self-pairing is the host-synthesised confirmation this design deliberately dropped (§4.5).
            var e = Assert.ThrowsExactly<InvalidDataException>(() => Topology("""
                                                                              { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
                                                                                "b": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" } }
                                                                              """));

            StringAssert.Contains(e.Message, "both endpoints are 'IoBlock.ActiveOutput'");
        }

        [TestMethod]
        public void RefuseTheSamePairDeclaredTwice()
        {
            const string Entry = """
                                 { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
                                   "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                                 """;

            // Declared once each way round — symmetric, so the second is the same wire, not a fan-out.
            var e = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyLoader.Build(Topology(Entry + """
                                                                                                              ,{ "a": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" },
                                                                                                                 "b": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" } }
                                                                                                              """)));

            StringAssert.Contains(e.Message, "are already paired");
        }

        [TestMethod]
        public void ResolveEachEndpointToItsOwnAutoCreatedEndpointAndSurviveASerializationRoundTrip()
        {
            var file = Topology("""
                                { "a": { "logicBlockName": "IoBlock", "contractIdentifier": "ActiveOutput" },
                                  "b": { "logicBlockName": "IdealIo", "contractIdentifier": "OutputChannel" } }
                                """);

            var pairing = DevTopologyLoader.Build(file).ContractPairings.Single();
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
        public void OmitPairingsFromAnUnpairedTopologyFile()
        {
            // The default is preserved byte for byte: a topology with no pairings serializes exactly as before.
            var file = DevTopologyFile.Parse($$"""
                                               { "id": "plain", "logicBlockInstances": [ { "typeFullName": "{{IoBlockType}}", "name": "IoBlock" } ] }
                                               """);

            Assert.IsNull(file.ContractPairings);
            StringAssert.DoesNotMatch(file.ToJson(), new System.Text.RegularExpressions.Regex("contractPairings"));
            Assert.IsEmpty(DevTopologyLoader.Build(file).ContractPairings);
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