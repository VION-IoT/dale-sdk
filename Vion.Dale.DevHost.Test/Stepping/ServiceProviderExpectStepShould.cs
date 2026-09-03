using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Contracts.Conventions;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Web;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The generic <c>serviceProviderExpect</c> assert step: asserts the value a block last wrote
    ///     on any <c>[ServiceProviderContractType]</c> value output contract, read from the generic output cache
    ///     the stand-in fills. Replaces <c>digitalOutput</c> / <c>analogOutput</c>; an input is drive-only.
    /// </summary>
    [TestClass]
    public class ServiceProviderExpectStepShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.1")]
        public async Task AssertOutputsDrivenAndAssertedThroughOneVocabulary()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            // Drive both HAL input families with serviceProviderSet, let the IoBlock's [Timer(1)] mirror them onto
            // its outputs, then assert both output families with serviceProviderExpect — the full generic loop.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect", "topology": "io",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true },
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "LevelInput" }, "value": 3.3 },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "equals": true } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "EchoOutput", "equals": 3.3, "tolerance": 0.001 } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("serviceProviderExpect", report.Steps[4].Kind);
            Assert.AreEqual("io.ActiveOutput", report.Steps[4].Target);
            Assert.AreEqual("== true", report.Steps[4].Argument);
            Assert.AreEqual("== 3.3 ±0.001", report.Steps[5].Argument);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.7")]
        public async Task HoldForEveryComparatorOnOutputs()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect-cmp", "topology": "io",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true },
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "LevelInput" }, "value": 3.3 },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "notEquals": false } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "oneOf": [true] } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "EchoOutput", "above": 3 } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "EchoOutput", "below": 4 } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.13")]
        public async Task FailLoudlyWhenOutputDoesNotHold()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            // ActiveOutput mirrors IsEnabled; we never enable it, so asserting it true must FAIL the run.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect-fail", "topology": "io",
                                                "steps": [
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "equals": true } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[1].Detail!, "expected io.ActiveOutput to equal true");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.4")]
        public async Task ResolveOutputAndRejectExpectOnInput()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            var outputErrors = new List<string>();

            // Act
            var output = resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "io", Contract = "ActiveOutput" } },
                                              "steps[0]",
                                              outputErrors);

            // Assert
            Assert.IsEmpty(outputErrors, string.Join("; ", outputErrors));
            Assert.IsNotNull(output.Contract);

            var inputErrors = new List<string>();
            resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "io", Contract = "EnableInput" } },
                                 "steps[0]",
                                 inputErrors);
            Assert.IsNotEmpty(inputErrors);
            StringAssert.Contains(inputErrors[0], "input");

            // An inbound-only handler writes no command, so the widened gate (VION-129) must still refuse this
            // one — and say which way out the author has, since "is an input" alone no longer implies "never
            // assertable".
            StringAssert.Contains(inputErrors[0], "nothing to assert");
            StringAssert.Contains(inputErrors[0], "serviceProviderSet");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.6")]
        [TestCategory("Smoke")]
        public async Task AssertEachFieldOfMultiFieldOutboundCommand()
        {
            // Arrange
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();

            // GridBlock mirrors the demand it received onto the GridSetpoint OUTPUT contract, whose command is a
            // multi-field struct: a bool, an enum, a nested struct, and a publish-time stamp. Every deterministic
            // field is asserted through `field`, including a nested one; issuedAt is deliberately left alone.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect-field", "topology": "grid",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "grid", "contract": "Demand" },
                                                    "value": { "valid": true, "scope": "PerPhase", "limits": { "activePowerW": 1500, "reactivePowerVar": 200 } } },
                                                  { "waitUntil": { "property": "grid.DemandValid", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "grid", "contract": "Setpoint", "field": "enforced", "equals": true } },
                                                  { "serviceProviderExpect": { "logicBlock": "grid", "contract": "Setpoint", "field": "scope", "equals": "PerPhase" } },
                                                  { "serviceProviderExpect": { "logicBlock": "grid", "contract": "Setpoint", "field": "limits.activePowerW", "equals": 1500, "tolerance": 0.01 } },
                                                  { "serviceProviderExpect": { "logicBlock": "grid", "contract": "Setpoint", "field": "limits.reactivePowerVar", "above": 100 } },
                                                  { "serviceProviderExpect": { "logicBlock": "grid", "contract": "Setpoint", "field": "Limits.ActivePowerW", "equals": 1500, "tolerance": 0.01 } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("grid.Setpoint.limits.activePowerW", report.Steps[5].Target, "the field belongs in the reported target, not only in the step body");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.6")]
        public async Task FailMultiFieldCommandWithNoFieldSelector()
        {
            // Arrange
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();

            // THE REGRESSION (VION-71). A whole multi-field command has no scalar leaf, so every comparator but
            // notEquals correctly fails against it — and notEquals passed, reporting a satisfied step having
            // compared nothing. It must now be refused, naming the fields that ARE addressable.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect-vacuous", "topology": "grid",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "grid", "contract": "Demand" },
                                                    "value": { "valid": true, "scope": "PerPhase", "limits": { "activePowerW": 1500, "reactivePowerVar": 200 } } },
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "grid", "contract": "Setpoint", "notEquals": "anything at all" } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            var error = report.ValidationErrors.Single();
            StringAssert.Contains(error, "multi-field command");
            StringAssert.Contains(error, "limits.activePowerW", StringComparison.Ordinal, "the error must name the fields that can be addressed");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.12")]
        public async Task FailOutputNeverWritten()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            // The cheaper half of the same defect, on a plain SCALAR output: nothing ever drove the block and no
            // advance fired its timer, so ActiveOutput was never written. notEquals used to pass against that
            // absent value; the step must now fail and say the contract was never written.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect-unwritten", "topology": "io",
                                                "steps": [
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "notEquals": false } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail!, "has not written this contract yet");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.6")]
        public async Task RejectMisspelledFieldAndOneLandingOnNestedStruct()
        {
            // Arrange
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            var misspelled = new List<string>();

            // Act
            resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "grid", Contract = "Setpoint", Field = "enforcd" } },
                                 "steps[0]",
                                 misspelled);

            // Assert
            Assert.IsNotEmpty(misspelled);
            StringAssert.Contains(misspelled[0], "has no field 'enforcd'");
            StringAssert.Contains(misspelled[0], "enforced", StringComparison.Ordinal, "the error must list what is addressable");

            // A nested struct is not itself addressable — only its scalar leaves are.
            var wholeStruct = new List<string>();
            resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "grid", Contract = "Setpoint", Field = "limits" } },
                                 "steps[0]",
                                 wholeStruct);
            Assert.IsNotEmpty(wholeStruct);
            StringAssert.Contains(wholeStruct[0], "has no field 'limits'");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.6")]
        public async Task RejectFieldOnSingleValueOutput()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            // SetDigitalOutput(bool) is single-field, so the codec unwraps it to a bare scalar on the wire: there
            // is nothing to address, and offering a field is an authoring mistake rather than a no-op.
            var errors = new List<string>();

            // Act
            resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "io", Contract = "ActiveOutput", Field = "value" } },
                                 "steps[0]",
                                 errors);

            // Assert
            Assert.IsNotEmpty(errors);
            StringAssert.Contains(errors[0], "writes a single value");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.12")]
        public async Task FailFieldAbsentFromCapturedCommand()
        {
            // Arrange
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();

            // The other half of the guard, and the one the resolver cannot pre-empt: `limits.activePowerW` IS an
            // addressable field of this command, so the step validates — but with no demand driven the block
            // writes no limits, and the field lands on nothing. That must fail and name the field, not compare a
            // literal against a null the runner never read.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect-absent-field", "topology": "grid",
                                                "steps": [
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "grid", "contract": "Setpoint", "field": "limits.activePowerW", "notEquals": 1500 } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[1].Detail!, "field 'limits.activePowerW' is not a scalar of the last written command");
            StringAssert.Contains(report.Steps[1].Detail!, "\"limits\":null", StringComparison.Ordinal, "the captured command is shown so the author can see why");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.8")]
        public async Task StandDownStaticFieldCheckWhenContractUndescribed()
        {
            // Arrange
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();

            // A contract the DevHost could not join to a handler type carries no field list. The resolver must
            // then accept whatever the author wrote and leave the verdict to the runner's read — not reject a
            // scenario it has no basis to judge.
            var configuration = host.Control.GetConfiguration();
            var contract = configuration.LogicBlocks.Single(b => b.Name == "grid").Contracts.Single(c => c.Identifier == "Setpoint");
            Assert.IsTrue(contract.Annotations.Remove("scenarioOutputFields"), "the fixture must have carried the annotation to begin with");

            var errors = new List<string>();

            // Act
            var resolved = new ScenarioResolver(configuration).ResolveStep(new ScenarioStep
                                                                           {
                                                                               ServiceProviderExpect = new ScenarioServiceProviderAssert
                                                                                                       { LogicBlock = "grid", Contract = "Setpoint", Field = "anything.at.all" },
                                                                           },
                                                                           "steps[0]",
                                                                           errors);

            // Assert
            Assert.IsEmpty(errors, string.Join("; ", errors));
            CollectionAssert.AreEqual(new[] { "anything", "at", "all" }, resolved.Contract!.FieldPath!.ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.9")]
        public async Task DemandNoFieldOfContractAlsoDrivable()
        {
            // Arrange
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();

            // A handler may declare both an inbound and an outbound ([ScenarioWire] sanctions "and/or"). A
            // serviceProviderSet has no field to give — the shape does not carry one — so the assert-side check
            // must not fire on the drive path, or the author is handed an error they cannot act on.
            var configuration = host.Control.GetConfiguration();
            var contract = configuration.LogicBlocks.Single(b => b.Name == "grid").Contracts.Single(c => c.Identifier == "Demand");
            contract.Annotations["scenarioOutputFields"] = new[] { "enforced", "limits.activePowerW" };

            var errors = new List<string>();

            // Act
            new ScenarioResolver(configuration).ResolveStep(new ScenarioStep { ServiceProviderSet = new ScenarioServiceProviderRef { LogicBlock = "grid", Contract = "Demand" } },
                                                            "steps[0]",
                                                            errors);

            // Assert
            Assert.IsEmpty(errors, string.Join("; ", errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.5")]
        [TestCategory("Smoke")]
        public async Task DriveAndAssertOneBidirectionalContractInOneScenario()
        {
            // Arrange
            await using var host = BuildSteppedPlantHost();
            await host.StartAsync();

            // VION-129. The classification is checked EXPLICITLY rather than inferred from a green run: if this
            // contract were classified an output the assert gate would never have been consulted and the run
            // below would prove nothing. IPlantControl declares no Consumers, so it carries no ZeroOrOne
            // annotation and reads as an INPUT; what makes it assertable is its handler's [ScenarioWire]
            // Outbound, surfaced as the scenarioOutputFields annotation on the same contract.
            var contract = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "plant").Contracts.Single(c => c.Identifier == "Control");
            Assert.IsFalse(contract.Annotations.ContainsKey(LogicBlockWiringConventions.ConsumersAnnotationKey),
                           "the fixture must classify as an INPUT — otherwise the widened assert gate is never exercised");
            CollectionAssert.AreEqual(new[] { "valid", "timestamp", "activePowerKw", "reactivePowerKvar" },
                                      ((IReadOnlyList<string>)contract.Annotations["scenarioOutputFields"]).ToArray(),
                                      "and it must carry the outbound command's leaves — the signal the gate widens on");

            // One contract identifier, driven and asserted in the same scenario, through the multi-field path.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-bidirectional", "topology": "plant",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "plant", "contract": "Control" },
                                                    "value": { "valid": true, "scope": "PerPhase", "supply": { "activePowerKw": 12.5, "reactivePowerKvar": 3.5 } } },
                                                  { "waitUntil": { "property": "plant.DemandValid", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "plant", "contract": "Control", "field": "valid", "equals": true } },
                                                  { "serviceProviderExpect": { "logicBlock": "plant", "contract": "Control", "field": "activePowerKw", "equals": 12.5, "tolerance": 0.001 } },
                                                  { "serviceProviderExpect": { "logicBlock": "plant", "contract": "Control", "field": "reactivePowerKvar", "above": 3 } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("plant.Control.valid", report.Steps[3].Target, "the assert names the field it read, on the same contract the first step drove");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.12")]
        public async Task ReadMultiFieldCommandAsUnreadableRatherThanNull()
        {
            // Arrange
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();

            var endpoint = SetpointEndpoint(host.Control);
            var beforeAnyWrite = host.Control.ReadServiceProviderOutput(endpoint.Sp, endpoint.Svc, endpoint.Contract);
            Assert.AreEqual(ServiceProviderOutputState.NeverWritten, beforeAnyWrite.State, "nothing has written the contract yet");
            Assert.IsNull(beforeAnyWrite.Captured);

            // Act
            await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                             {
                                                               "version": 1, "id": "sp-read", "topology": "grid",
                                                               "steps": [
                                                                 { "serviceProviderSet": { "logicBlock": "grid", "contract": "Demand" },
                                                                   "value": { "valid": true, "scope": "PerPhase", "limits": { "activePowerW": 1500, "reactivePowerVar": 200 } } },
                                                                 { "advance": { "seconds": 1 } }
                                                               ]
                                                             }
                                                             """),
                                          host.Control);

            // The whole command: written, but with no scalar leaf — the state the old surface collapsed into the
            // same null as "never written", which is what let notEquals pass.
            var whole = host.Control.ReadServiceProviderOutput(endpoint.Sp, endpoint.Svc, endpoint.Contract);

            // Assert
            Assert.AreEqual(ServiceProviderOutputState.Unreadable, whole.State);
            Assert.IsNotNull(whole.Captured, "the captured command is carried so a failing assert can show it");
            StringAssert.Contains(whole.Captured!, "activePowerW");

            // A field: readable, case-insensitively, through the nested struct.
            var nested = host.Control.ReadServiceProviderOutput(endpoint.Sp, endpoint.Svc, endpoint.Contract, ["Limits", "ActivePowerW"]);
            Assert.AreEqual(ServiceProviderOutputState.Readable, nested.State);
            Assert.AreEqual(1500d, nested.Value);

            // A field that is not there reads as unreadable, never as a null that compares equal to nothing.
            var missing = host.Control.ReadServiceProviderOutput(endpoint.Sp, endpoint.Svc, endpoint.Contract, ["nope"]);
            Assert.AreEqual(ServiceProviderOutputState.Unreadable, missing.State);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.6")]
        [TestCategory("Smoke")]
        public async Task CarryAddressableFieldsOverHttpWhereEditorReadsThem()
        {
            // Arrange
            var port = FreePort();
            await using var host = DevHostBuilder.Create()
                                                 .WithDi<SmokeHost.DependencyInjection>()
                                                 .WithConfiguration(DevConfigurationBuilder.Create()
                                                                                           .WithTopologyName("grid")
                                                                                           .AddLogicBlock<SmokeHost.LogicBlocks.GridBlock>("grid")
                                                                                           .AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io")
                                                                                           .Build())
                                                 .WithDeterministicStepping()
                                                 .WithWebUi(port)
                                                 .Build();
            await host.StartAsync();

            // `scenarioOutputFields` is a literal in three places — the C# writer, the CLI validator, and the
            // SPA editor's picker — and nothing else checks they agree. This pins the wire half: the key, its
            // camelCase leaf paths, and the EMPTY list that tells the editor a scalar output takes no field.
            // Act
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            using var configuration = JsonDocument.Parse(await client.GetStringAsync("/api/configuration"));
            var blocks = configuration.RootElement.GetProperty("logicBlocks").EnumerateArray().ToList();

            // Assert
            var fields = Contract(blocks, "grid", "Setpoint").GetProperty("annotations").GetProperty("scenarioOutputFields");
            CollectionAssert.AreEqual(new[] { "enforced", "scope", "limits.activePowerW", "limits.reactivePowerVar", "issuedAt" },
                                      fields.EnumerateArray().Select(f => f.GetString()).ToArray(),
                                      "the multi-field command's addressable leaves, in wire keys");

            Assert.AreEqual(0,
                            Contract(blocks, "io", "ActiveOutput").GetProperty("annotations").GetProperty("scenarioOutputFields").GetArrayLength(),
                            "a single-value output carries an EMPTY list — the editor reads that as 'no field to address', not as 'undescribed'");

            Assert.IsFalse(Contract(blocks, "grid", "Demand").GetProperty("annotations").TryGetProperty("scenarioOutputFields", out _),
                           "an input contract has nothing to assert, so it carries no field list at all");

            // The mirror key, and the drive gate (VION-131): its PRESENCE is what the resolver, the CLI
            // validator and the page's JS all read as "this contract is drivable". Same three-way agreement.
            var inbound = Contract(blocks, "grid", "Demand").GetProperty("annotations").GetProperty("scenarioInputFields");
            CollectionAssert.AreEqual(new[] { "valid", "scope", "limits.activePowerW", "limits.reactivePowerVar" },
                                      inbound.EnumerateArray().Select(f => f.GetString()).ToArray(),
                                      "the inbound wire struct's leaves, in wire keys");

            Assert.AreEqual(0,
                            Contract(blocks, "io", "ActiveOutput").GetProperty("annotations").GetProperty("scenarioInputFields").GetArrayLength(),
                            "an output confirmed by its provider IS drivable, and its confirmation is a bare scalar — an EMPTY list, not an absent key");

            Assert.IsFalse(Contract(blocks, "grid", "Setpoint").GetProperty("annotations").TryGetProperty("scenarioInputFields", out _),
                           "an outbound-only contract carries no inbound list at all — the absence is what refuses a serviceProviderSet");

            static JsonElement Contract(List<JsonElement> blocks, string block, string contract)
            {
                return blocks.Single(b => b.GetProperty("name").GetString() == block)
                             .GetProperty("contracts")
                             .EnumerateArray()
                             .Single(c => c.GetProperty("identifier").GetString() == contract);
            }
        }

        private static IDevHost BuildSteppedIoHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("io").AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io").Build();
            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        private static IDevHost BuildSteppedGridHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("grid").AddLogicBlock<SmokeHost.LogicBlocks.GridBlock>("grid").Build();
            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        // The VION-129 fixture: PlantBlock's Control contract carries BOTH wire directions under one identifier.
        private static IDevHost BuildSteppedPlantHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("plant").AddLogicBlock<SmokeHost.LogicBlocks.PlantBlock>("plant").Build();
            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        // The mocked endpoint GridBlock's Setpoint contract is auto-mapped onto, for the direct control-surface
        // reads (a scenario addresses it by logicBlock + contract; the control surface by the endpoint ids).
        private static (string Sp, string Svc, string Contract) SetpointEndpoint(IDevHostControl control)
        {
            var mapping = control.GetConfiguration().LogicBlocks.Single(b => b.Name == "grid").ContractMappings.Single(m => m.ContractIdentifier == "Setpoint");

            return (mapping.MappedServiceProviderIdentifier, mapping.MappedServiceIdentifier, mapping.MappedContractIdentifier);
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }

        private static string Join(ScenarioRunReport report)
        {
            var steps = report.Setup.Concat(report.Steps).Select(s => $"[{s.Index} {s.Kind} {s.Status}: {s.Detail}]");
            return string.Join("; ", report.ValidationErrors.Concat(steps));
        }
    }
}