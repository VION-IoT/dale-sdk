using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit
{
    /// <summary>
    ///     Wires a <see cref="FakeModbusTcpClientProxy" /> and <see cref="SynchronousRequestQueue" /> into
    ///     a real <see cref="LogicBlockModbusTcpClient" /> (with the SDK's real <c>ModbusTcpClientWrapper</c>
    ///     between them). The result is a fully wired <see cref="ILogicBlockModbusTcpClient" /> that the
    ///     SUT can be constructed with — every byte / word-order conversion runs through real SDK code,
    ///     and the fake's in-memory state is the only thing being substituted.
    ///     <code>
    ///     var harness = new FakeModbusTcpHarness();
    ///     harness.Proxy.SetHoldingRegisters(unitId: 1, startingAddress: 40000, registerBytes: [0x12, 0x34, 0x56, 0x78]);
    ///     var sut = new MyBlock(harness.Client, new Mock&lt;ILogger&gt;().Object);
    ///     var ctx = sut.CreateTestContext().Build();
    ///     sut.Tick();
    ///     ctx.FlushPendingActions();
    ///     Assert.AreEqual(0x12345678u, sut.Power);
    ///     </code>
    ///     <para>
    ///         <b>Why an internal service provider:</b> <c>ModbusTcpClientWrapper</c>, <c>RequestFactory</c>,
    ///         and <c>BitConverterProxy</c> are <c>internal</c> to their assemblies, so the TestKit cannot
    ///         <c>new</c> them directly. Calling <see cref="ServiceCollectionExtensions.AddDaleModbusTcpSdk" />
    ///         constructs the full production graph; we then override just <see cref="IModbusTcpClientProxy" />
    ///         (the fake) and <see cref="IRequestQueue" /> (the synchronous queue) — last-registered-wins on
    ///         <see cref="ServiceCollection" /> picks our overrides. Everything between the SUT and the fake
    ///         is real SDK code: the same <c>ModbusTcpClientWrapper</c> that ships, exercising the same
    ///         byte / word-order conversion path.
    ///     </para>
    /// </summary>
    [PublicApi]
    public sealed class FakeModbusTcpHarness : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        public FakeModbusTcpHarness() : this(new FakeModbusTcpClientProxy())
        {
        }

        public FakeModbusTcpHarness(FakeModbusTcpClientProxy proxy)
        {
            Proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));

            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            // Pull in the full production registration graph (Core + Tcp wrapper + real RequestFactory),
            // then override the two points where we want test substitutions: the byte-level proxy and
            // the queue. ServiceCollection's last-registered-wins semantics for GetRequiredService<T>
            // means the overrides take effect; everything in between stays the real SDK code path.
            services.AddDaleModbusTcpSdk();
            services.AddSingleton<IModbusTcpClientProxy>(Proxy);
            services.AddSingleton<IRequestQueue, SynchronousRequestQueue>();

            _serviceProvider = services.BuildServiceProvider();
            Client = _serviceProvider.GetRequiredService<ILogicBlockModbusTcpClient>();
        }

        /// <summary>
        ///     The fake proxy — pre-populate registers / coils via its <c>SetX</c> methods, inject faults
        ///     via its <c>EnqueueX</c> methods, and inspect what happened via its <c>ReadHistory</c> /
        ///     <c>WriteHistory</c> / <c>ConnectionHistory</c> properties or the <c>Verify*</c> extensions.
        /// </summary>
        public FakeModbusTcpClientProxy Proxy { get; }

        /// <summary>The fully wired client to inject into the SUT. Uses real SDK conversion against the fake proxy.</summary>
        public ILogicBlockModbusTcpClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            _serviceProvider.Dispose();
        }
    }
}
