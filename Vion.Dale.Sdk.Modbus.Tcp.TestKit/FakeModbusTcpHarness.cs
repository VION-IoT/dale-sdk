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
    ///     Wires a <see cref="FakeModbusTcpClientProxy" /> and <see cref="SynchronousRequestQueue" /> into a
    ///     real <c>LogicBlockModbusTcpClient</c>, so the SUT exercises real SDK byte / word-order conversion
    ///     against the fake's in-memory state.
    ///     <code>
    ///     var harness = new FakeModbusTcpHarness();
    ///     harness.Proxy.SetHoldingRegisters(unitId: 1, startingAddress: 40000, registerBytes: [0x12, 0x34, 0x56, 0x78]);
    ///     var sut = new MyBlock(harness.Client, new Mock&lt;ILogger&gt;().Object);
    ///     var ctx = sut.CreateTestContext().Build();
    ///     sut.Tick();
    ///     ctx.FlushPendingActions();
    ///     Assert.AreEqual(0x12345678u, sut.Power);
    ///     </code>
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

            // The internal ServiceProvider is here for a specific reason: ModbusTcpClientWrapper,
            // RequestFactory, and BitConverterProxy are internal to their assemblies, so the TestKit
            // cannot construct them directly. AddDaleModbusTcpSdk() builds the production graph; we
            // then override only the two layers we want to fake (the byte-level proxy and the queue).
            // ServiceCollection's last-registered-wins semantics for GetRequiredService<T> picks our
            // overrides; everything in between stays real SDK code.
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddDaleModbusTcpSdk();
            services.AddSingleton<IModbusTcpClientProxy>(Proxy);
            services.AddSingleton<IRequestQueue, SynchronousRequestQueue>();

            _serviceProvider = services.BuildServiceProvider();
            Client = _serviceProvider.GetRequiredService<ILogicBlockModbusTcpClient>();
        }

        /// <summary>The fake proxy — pre-populate registers, inject faults, inspect histories.</summary>
        public FakeModbusTcpClientProxy Proxy { get; }

        /// <summary>The fully wired client to inject into the SUT.</summary>
        public ILogicBlockModbusTcpClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            _serviceProvider.Dispose();
        }
    }
}
