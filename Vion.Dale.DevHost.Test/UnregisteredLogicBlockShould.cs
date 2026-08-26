using System;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Topologies;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Regression (VION-66): a topology block whose type is missing its <c>services.AddTransient&lt;T&gt;()</c>
    ///     line must stop the host at resolution with a message naming the type, the topology id and the fix.
    ///     Before the fix, <c>DevHostIntrospection.Introspect</c> logged one warning and skipped the block, which
    ///     surfaced as <see cref="System.Collections.Generic.KeyNotFoundException" /> from
    ///     <c>BuildLogicBlock</c> plus a per-tick "unknown service id" warning flood.
    /// </summary>
    [TestClass]
    public class UnregisteredLogicBlockShould
    {
        [TestMethod]
        public async Task FailTheHost_WhenWiredViaTheFluentBuilder()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("unregistered").AddLogicBlock<UnregisteredBlock>("gadget").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

            var id = config.LogicBlocks[0].Id;
            StringAssert.Contains(exception.Message, typeof(UnregisteredBlock).FullName!, "the message must name the offending type");
            StringAssert.Contains(exception.Message, id, "the message must name the topology id so the block is findable in the topology file");
            StringAssert.Contains(exception.Message, "'gadget'", "the message must name the instance");
            StringAssert.Contains(exception.Message, "AddTransient<UnregisteredBlock>()", "the message must name the fix");
            StringAssert.Contains(exception.Message, "IConfigureServices", "the message must say where the fix goes");
        }

        [TestMethod]
        public async Task FailTheHost_WhenWiredViaATopologyFile()
        {
            // The second entry path (RFC 0006 R5). It converges on the same GetService resolution, but a fix
            // that only guarded the fluent builder would leave this half of the bug alive.
            var file = DevTopologyFile.Parse($$"""
                                               {
                                                 "id": "unregistered",
                                                 "logicBlockInstances": [
                                                   { "typeFullName": "{{typeof(UnregisteredBlock).FullName}}", "name": "gadget" }
                                                 ]
                                               }
                                               """);
            var config = DevTopologyLoader.Build(file);
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

            StringAssert.Contains(exception.Message, typeof(UnregisteredBlock).FullName!, "the message must name the offending type");
        }

        [TestMethod]
        public async Task ReportEveryUnregisteredBlockAtOnce_NotJustTheFirst()
        {
            // Collect-then-report, like the parser (Vion.Dale.LogicBlockParser/Program.cs): three bad blocks
            // give one message listing all three, so a topology is fixed in one pass rather than one run each.
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("unregistered")
                                                .AddLogicBlock<UnregisteredBlock>("first")
                                                .AddLogicBlock<CounterBlock>("counter")
                                                .AddLogicBlock<SecondUnregisteredBlock>("second")
                                                .Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();

            var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

            StringAssert.Contains(exception.Message, "'first'", "the first unregistered block must be listed");
            StringAssert.Contains(exception.Message, "'second'", "the second unregistered block must be listed too — not just the first");
            Assert.IsFalse(exception.Message.Contains(nameof(CounterBlock), StringComparison.Ordinal), "a registered block must not be listed");
        }

        [TestMethod]
        public async Task FailTheSelfIntrospectingPath_RatherThanThrowingKeyNotFound()
        {
            // GetConfiguration before StartAsync self-introspects (HeadlessControlShould
            // .GetConfiguration_BeforeStart_SelfIntrospects). That path used to be where the skip surfaced as
            // KeyNotFoundException; it must now surface the same actionable failure as startup.
            var config = DevConfigurationBuilder.Create().WithTopologyName("unregistered").AddLogicBlock<UnregisteredBlock>("gadget").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();

            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => host.Control.GetConfiguration());

            StringAssert.Contains(exception.Message, typeof(UnregisteredBlock).FullName!, "the message must name the offending type");
        }
    }
}