using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Contracts.Conventions;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The generic <c>serviceProviderSet</c> drive step (RFC 0010): one step kind drives any
    ///     <c>[ServiceProviderContractType]</c> value contract whose handler declares a <c>[ScenarioWire]</c>
    ///     Inbound. That declaration, not the contract's multiplicity, is the gate, so an output confirmed by
    ///     its provider is drivable too; the drive is routed to the generic stand-in registered under the
    ///     contract's <c>ContractHandlerActorName</c>.
    ///     <para>
    ///         Cross-tier: <c>AC-SCEN-007.3</c> is proven here at the resolver, where the multiplicity is read
    ///         explicitly so the re-keyed gate is exercised, and by the committed <c>output-confirmation</c>
    ///         scenario, which drives a real provider confirmation end to end.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ServiceProviderSetStepShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.1")]
        public async Task DriveValueInputContractReachingConsumingBlock()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-set", "topology": "io",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "expect": { "property": "io.IsEnabled", "equals": true } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert - the drive is addressed as (block, contract), and the report echoes that addressing.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("serviceProviderSet", report.Steps[0].Kind);
            Assert.AreEqual("io.EnableInput", report.Steps[0].Target);
            Assert.AreEqual("true", report.Steps[0].Argument);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.3")]
        public async Task ResolveDriveOnDeclaredInboundNotOnContractsMultiplicity()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();
            var configuration = host.Control.GetConfiguration();
            var resolver = new ScenarioResolver(configuration);

            // Act / Assert
            // EnableInput is a value input (ZeroOrMore) → drivable; resolves to its generic stand-in name.
            var inputErrors = new List<string>();
            var input = resolver.ResolveStep(new ScenarioStep { ServiceProviderSet = new ScenarioServiceProviderRef { LogicBlock = "io", Contract = "EnableInput" } },
                                             "steps[0]",
                                             inputErrors);
            Assert.IsEmpty(inputErrors, string.Join("; ", inputErrors));
            Assert.IsNotNull(input.Contract);
            Assert.AreEqual("DigitalInputHandler", input.Contract!.HandlerName);

            // VION-131. ActiveOutput is a single-writer output (ZeroOrOne) and is drivable ANYWAY, because its
            // handler declares an inbound — the provider's confirmation. The multiplicity is checked explicitly:
            // were the fixture to stop classifying as an output, the re-keyed gate would never be exercised.
            var contract = configuration.LogicBlocks.Single(b => b.Name == "io").Contracts.Single(c => c.Identifier == "ActiveOutput");
            Assert.AreEqual(LogicBlockWiringConventions.ZeroOrOne,
                            contract.Annotations[LogicBlockWiringConventions.ConsumersAnnotationKey] as string,
                            "the fixture must classify as an OUTPUT — otherwise this proves nothing about the re-key");

            var outputErrors = new List<string>();
            var output = resolver.ResolveStep(new ScenarioStep { ServiceProviderSet = new ScenarioServiceProviderRef { LogicBlock = "io", Contract = "ActiveOutput" } },
                                              "steps[0]",
                                              outputErrors);
            Assert.IsEmpty(outputErrors, string.Join("; ", outputErrors));
            Assert.AreEqual("DigitalOutputHandler", output.Contract!.HandlerName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.2")]
        public async Task RefuseDriveOnContractWhoseHandlerDeclaresNoInbound()
        {
            // Arrange
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();

            // GridSetpointHandler declares an Outbound only, so nothing could ever be delivered to the block on
            // it — the drive is refused at resolve time rather than dropped by the stand-in mid-run. This is the
            // gate `dale scenario validate` mirrors, so the message has to say what is wrong and what to do.

            // Act
            var errors = new List<string>();
            new ScenarioResolver(host.Control.GetConfiguration()).ResolveStep(new ScenarioStep
                                                                              {
                                                                                  ServiceProviderSet =
                                                                                      new ScenarioServiceProviderRef
                                                                                      { LogicBlock = "grid", Contract = "Setpoint" },
                                                                              },
                                                                              "steps[0]",
                                                                              errors);

            // Assert
            Assert.IsNotEmpty(errors);
            StringAssert.Contains(errors[0], "cannot be driven");
            StringAssert.Contains(errors[0], "Inbound");
            StringAssert.Contains(errors[0], "serviceProviderExpect", StringComparison.Ordinal, "the error must name the operation that DOES work on it");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        [TestProperty("spec", "AC-SCEN-007.3")]
        public async Task DeliverOutputConfirmationIncludingDeliberatelyMismatchedOne()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            // Off production, DigitalOutputChanged was constructed nowhere a DevHost bench could reach,
            // so a block's OutputChanged subscription was structurally dead. Driving the output contract's
            // declared inbound is that confirmation — first truthfully, then with a value the block never
            // commanded, which is the diagnostic real hardware cannot be asked to produce.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-confirm", "topology": "io",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true },
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "LevelInput" }, "value": 3.3 },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "equals": true } },
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "ActiveOutput" }, "value": true },
                                                  { "waitUntil": { "property": "io.ConfirmedActive", "equals": true }, "timeoutSeconds": 5 },
                                                  { "expect": { "property": "io.ConfirmationMismatch", "equals": false } },
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EchoOutput" }, "value": 3.3 },
                                                  { "waitUntil": { "property": "io.ConfirmedEcho", "equals": 3.3, "tolerance": 0.001 }, "timeoutSeconds": 5 },
                                                  { "expect": { "property": "io.ConfirmationMismatch", "equals": false } },
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "ActiveOutput" }, "value": false },
                                                  { "waitUntil": { "property": "io.ConfirmationMismatch", "equals": true }, "timeoutSeconds": 5 },
                                                  { "expect": { "property": "io.ConfirmedActive", "equals": false } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "equals": true } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("serviceProviderSet", report.Steps[5].Kind);
            Assert.AreEqual("io.ActiveOutput", report.Steps[5].Target, "the drive addresses the same contract identifier the assert reads");

            // The last step is the separation guard: a confirmation is an INBOUND and must not touch the output
            // cache. serviceProviderExpect still reads what the BLOCK commanded (true), not the false the
            // provider last confirmed — if the two ever shared a slot, that step fails and the run is red.
            Assert.AreEqual("== true", report.Steps[14].Argument);
        }

        private static IDevHost BuildSteppedIoHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("io").AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io").Build();
            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        // GridBlock's Setpoint contract is the fixture's one-directional OUTPUT: its handler declares an
        // Outbound and no Inbound, which is what makes it undrivable.
        private static IDevHost BuildSteppedGridHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("grid").AddLogicBlock<SmokeHost.LogicBlocks.GridBlock>("grid").Build();
            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        private static string Join(ScenarioRunReport report)
        {
            var steps = report.Setup.Concat(report.Steps).Select(s => $"[{s.Index} {s.Kind} {s.Status}: {s.Detail}]");
            return string.Join("; ", report.ValidationErrors.Concat(steps));
        }
    }
}