using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit
{
    /// <summary>
    ///     Wires a <see cref="FakeModbusTcpServerProxy" /> into a real <c>LogicBlockModbusTcpServer</c>, so the
    ///     SUT exercises real SDK extent validation and byte / word-order conversion against the fake's in-memory
    ///     register store — no sockets, no free-port dance.
    ///     <code>
    ///     var harness = new FakeModbusTcpServerHarness();
    ///     var sut = new MyServerBlock(/* inject a factory returning */ harness.Server);
    ///     harness.Client.WriteSingleHoldingRegister(address: 0, value: 42);   // act as the master
    ///     sut.Tick();
    ///     CollectionAssert.AreEqual(expectedWireBytes, harness.Client.ReadInputRegistersRaw(0, 2));
    ///     </code>
    /// </summary>
    [PublicApi]
    public sealed class FakeModbusTcpServerHarness : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        /// <summary>The fake proxy — inspect or pre-populate the register buffers, shape diagnostics.</summary>
        public FakeModbusTcpServerProxy Proxy { get; }

        /// <summary>The fully wired server to inject into the SUT (or hand out via <see cref="ServerFactory" />).</summary>
        public ILogicBlockModbusTcpServer Server { get; }

        /// <summary>A factory handing out <see cref="Server" /> — for blocks that are factory-injected.</summary>
        public ILogicBlockModbusTcpServerFactory ServerFactory { get; }

        /// <summary>The test-side master view driving the wire side of the server.</summary>
        public FakeModbusTcpServerClient Client { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FakeModbusTcpServerHarness" /> class with a fresh fake proxy.
        /// </summary>
        public FakeModbusTcpServerHarness() : this(new FakeModbusTcpServerProxy())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FakeModbusTcpServerHarness" /> class with the given fake proxy.
        /// </summary>
        /// <param name="proxy">The fake proxy backing the server's register buffers.</param>
        public FakeModbusTcpServerHarness(FakeModbusTcpServerProxy proxy)
        {
            Proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));

            // Same pattern as FakeModbusTcpHarness: LogicBlockModbusTcpServer is internal to its assembly, so the
            // TestKit cannot construct it directly. AddDaleModbusTcpSdk() builds the production graph; we then
            // override only the byte-level proxy. ServiceCollection's last-registered-wins semantics for
            // GetRequiredService<T> picks our override; everything in between stays real SDK code.
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddDaleModbusTcpSdk();
            services.AddSingleton<IModbusTcpServerProxy>(Proxy);

            _serviceProvider = services.BuildServiceProvider();
            Server = _serviceProvider.GetRequiredService<ILogicBlockModbusTcpServer>();
            ServerFactory = new FixedServerFactory(Server);
            Client = new FakeModbusTcpServerClient(Proxy);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Server.Dispose();
            _serviceProvider.Dispose();
        }

        private sealed class FixedServerFactory : ILogicBlockModbusTcpServerFactory
        {
            private readonly ILogicBlockModbusTcpServer _server;

            public FixedServerFactory(ILogicBlockModbusTcpServer server)
            {
                _server = server;
            }

            public ILogicBlockModbusTcpServer Create()
            {
                return _server;
            }
        }
    }
}