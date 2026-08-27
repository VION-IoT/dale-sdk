using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The generic <c>serviceProviderExpect</c> assert step (RFC 0010): asserts the value a block last wrote
    ///     on any <c>[ServiceProviderContractType]</c> value output contract, read from the generic output cache
    ///     the stand-in fills. Replaces <c>digitalOutput</c> / <c>analogOutput</c>; an input is drive-only.
    /// </summary>
    [TestClass]
    public class ServiceProviderExpectStepShould
    {
        [TestMethod]
        public async Task AssertOutputs_DrivenAndAssertedEntirelyThroughTheGenericVocabulary()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("serviceProviderExpect", report.Steps[4].Kind);
            Assert.AreEqual("io.ActiveOutput", report.Steps[4].Target);
            Assert.AreEqual("== true", report.Steps[4].Argument);
            Assert.AreEqual("== 3.3 ±0.001", report.Steps[5].Argument);
        }

        [TestMethod]
        public async Task SupportEveryComparator_OnOutputs()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        public async Task FailLoudly_WhenTheOutputDoesNotHold()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[1].Detail!, "expected io.ActiveOutput to equal true");
        }

        [TestMethod]
        public async Task ResolveAnOutput_AndRejectAnExpectOnAnInput()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            var outputErrors = new List<string>();
            var output = resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "io", Contract = "ActiveOutput" } },
                                              "steps[0]",
                                              outputErrors);
            Assert.IsEmpty(outputErrors, string.Join("; ", outputErrors));
            Assert.IsNotNull(output.Contract);

            var inputErrors = new List<string>();
            resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "io", Contract = "EnableInput" } },
                                 "steps[0]",
                                 inputErrors);
            Assert.IsNotEmpty(inputErrors);
            StringAssert.Contains(inputErrors[0], "input");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task AssertEachDeterministicField_OfAMultiFieldOutboundCommand()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("grid.Setpoint.limits.activePowerW", report.Steps[5].Target, "the field belongs in the reported target, not only in the step body");
        }

        [TestMethod]
        public async Task FailAMultiFieldCommandWithNoFieldSelector_RatherThanPassNotEqualsVacuously()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            var error = report.ValidationErrors.Single();
            StringAssert.Contains(error, "multi-field command");
            StringAssert.Contains(error, "limits.activePowerW", StringComparison.Ordinal, "the error must name the fields that can be addressed");
        }

        [TestMethod]
        public async Task FailAnOutputTheBlockNeverWrote_RatherThanPassNotEqualsVacuously()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail!, "has not written this contract yet");
        }

        [TestMethod]
        public async Task RejectAMisspelledField_AndAFieldOnASingleValueOutput()
        {
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            var misspelled = new List<string>();
            resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "grid", Contract = "Setpoint", Field = "enforcd" } },
                                 "steps[0]",
                                 misspelled);
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
        public async Task RejectAFieldOnASingleValueOutput()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            // SetDigitalOutput(bool) is single-field, so the codec unwraps it to a bare scalar on the wire: there
            // is nothing to address, and offering a field is an authoring mistake rather than a no-op.
            var errors = new List<string>();
            resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "io", Contract = "ActiveOutput", Field = "value" } },
                                 "steps[0]",
                                 errors);

            Assert.IsNotEmpty(errors);
            StringAssert.Contains(errors[0], "writes a single value");
        }

        [TestMethod]
        public async Task ReadAMultiFieldCommand_AsUnreadableRatherThanNull()
        {
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();

            var endpoint = SetpointEndpoint(host.Control);
            var beforeAnyWrite = host.Control.ReadServiceProviderOutput(endpoint.Sp, endpoint.Svc, endpoint.Contract);
            Assert.AreEqual(ServiceProviderOutputState.NeverWritten, beforeAnyWrite.State, "nothing has written the contract yet");
            Assert.IsNull(beforeAnyWrite.Captured);

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
            Assert.AreEqual(ServiceProviderOutputState.Unreadable, whole.State);
            Assert.IsNotNull(whole.Captured, "the captured command is carried so a failing assert can show it");
            StringAssert.Contains(whole.Captured!, "activePowerW");
            Assert.IsNull(host.Control.GetServiceProviderOutput(endpoint.Sp, endpoint.Svc, endpoint.Contract), "the scalar convenience read is unchanged");

            // A field: readable, case-insensitively, through the nested struct.
            var nested = host.Control.ReadServiceProviderOutput(endpoint.Sp, endpoint.Svc, endpoint.Contract, ["Limits", "ActivePowerW"]);
            Assert.AreEqual(ServiceProviderOutputState.Readable, nested.State);
            Assert.AreEqual(1500d, nested.Value);

            // A field that is not there reads as unreadable, never as a null that compares equal to nothing.
            var missing = host.Control.ReadServiceProviderOutput(endpoint.Sp, endpoint.Svc, endpoint.Contract, ["nope"]);
            Assert.AreEqual(ServiceProviderOutputState.Unreadable, missing.State);
        }

        [TestMethod]
        public void RejectAServiceProviderExpect_InSetup()
        {
            var error = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                               { "version": 1, "id": "bad", "topology": "io",
                                                                                                 "setup": [ { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "equals": true } } ] }
                                                                                               """));
            Assert.IsTrue(error.Errors.Any(e => e.Contains("setup entries stage state")), error.Message);
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

        // The mocked endpoint GridBlock's Setpoint contract is auto-mapped onto, for the direct control-surface
        // reads (a scenario addresses it by logicBlock + contract; the control surface by the endpoint ids).
        private static (string Sp, string Svc, string Contract) SetpointEndpoint(IDevHostControl control)
        {
            var mapping = control.GetConfiguration().LogicBlocks.Single(b => b.Name == "grid").ContractMappings.Single(m => m.ContractIdentifier == "Setpoint");

            return (mapping.MappedServiceProviderIdentifier, mapping.MappedServiceIdentifier, mapping.MappedContractIdentifier);
        }

        private static string Join(ScenarioRunReport report)
        {
            var steps = report.Setup.Concat(report.Steps).Select(s => $"[{s.Index} {s.Kind} {s.Status}: {s.Detail}]");
            return string.Join("; ", report.ValidationErrors.Concat(steps));
        }
    }
}