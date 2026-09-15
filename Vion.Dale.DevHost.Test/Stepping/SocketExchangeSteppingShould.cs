using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     A stepped host whose blocks talk Modbus TCP and HTTP over real loopback sockets. The exchange leaves the actor
    ///     system between the request and its callback, so mailbox depth and the handler count both read zero while it is
    ///     on the wire; these tests pin that a stepped advance still waits for it.
    ///     <para>
    ///         The peers are test-owned raw sockets that hold their first answer until the test has taken its read, or until
    ///         a half-second fallback passes (<see cref="HeldAnswerPeer" />). Holding is what makes the window deterministic:
    ///         a
    ///         loopback round trip is sub-millisecond, so without it a settle that ignores the exchange would still usually
    ///         lose the race and pass. With the exchange counted, the advance cannot return while the answer is held, so the
    ///         fallback releases it and the read after the advance sees the answer; without, the advance returns first and the
    ///         read sees the block's initial value. The fallback is not free of the outcome: a settle without the accounting
    ///         would pass if the advance took longer than the fallback to return.
    ///     </para>
    ///     <para>
    ///         Cross-tier: the hosted HTTP server's case (<c>AC-HTTP-018.2</c>) is carried here end to end without a hold —
    ///         nothing a block or a test can reach parks the server between writing its response and recording the request.
    ///         The transport's own ordering, that its exchange closes only after the request is recorded, is pinned where the
    ///         transport is visible, in <c>Vion.Dale.Sdk.Http.Test</c>.
    ///     </para>
    /// </summary>
    [TestClass]
    public class SocketExchangeSteppingShould
    {
        private static readonly DateTimeOffset Epoch = new(2026,
                                                           1,
                                                           1,
                                                           0,
                                                           0,
                                                           0,
                                                           TimeSpan.Zero);

        // The longest a held answer waits for the test's read before the peer releases it anyway — under the Modbus
        // client's one-second default operation timeout, which would otherwise end the held exchange as a failure.
        private static readonly TimeSpan Fallback = TimeSpan.FromMilliseconds(500);

        // A hang guard for waits expected to complete.
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.5")]
        [TestProperty("spec", "AC-MODB-020.1")]
        public async Task DeliverModbusReadIssuedDuringAdvanceBeforeAdvanceReturns()
        {
            // Arrange — the poller reads at t = 1 s, inside the advance.
            await using var peer = HeldAnswerPeer.Modbus(42, Fallback);
            await using var host = SteppedHost(builder => builder.AddLogicBlock<ModbusPollerBlock>("poller"));
            await host.StartAsync();
            await host.Control.SetPropertyAsync("poller", "Port", peer.Port);

            // Act
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1)).WaitAsync(Timeout);
            var register = (int)host.Control.GetProperty("poller", "Register")!;
            peer.Release();

            // Assert
            Assert.IsTrue(peer.RequestArrived.IsCompleted);
            Assert.AreEqual(42, register);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-018.1")]
        public async Task DeliverHttpCallbackIssuedOutsideAdvanceBeforeNextAdvanceReturns()
        {
            // Arrange — the fetch is issued by the control write, before any advance begins.
            await using var peer = HeldAnswerPeer.Http("42", Fallback);
            await using var host = SteppedHost(builder => builder.AddLogicBlock<HttpFetcherBlock>("fetcher"));
            await host.StartAsync();
            await host.Control.SetPropertyAsync("fetcher", "Url", $"http://127.0.0.1:{peer.Port}/value");
            await peer.RequestArrived.WaitAsync(Timeout);

            // Act — a zero budget moves no virtual time; it only settles.
            await host.Control.AdvanceAsync(TimeSpan.Zero).WaitAsync(Timeout);
            var fetched = (int)host.Control.GetProperty("fetcher", "Fetched")!;
            peer.Release();

            // Assert
            Assert.AreEqual(42, fetched);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.11")]
        public async Task NameHeldExchangeWhenQuiescenceBudgetIsSpent()
        {
            // Arrange — a budget shorter than the client's own one-second operation timeout, and a peer that never answers
            // on its own, so the budget is what ends the wait.
            await using var peer = HeldAnswerPeer.Modbus(42, System.Threading.Timeout.InfiniteTimeSpan);
            await using var host = SteppedHost(builder => builder.AddLogicBlock<ModbusPollerBlock>("poller"), TimeSpan.FromMilliseconds(400));
            await host.StartAsync();
            await host.Control.SetPropertyAsync("poller", "Port", peer.Port);

            // Act
            var refusal = await Assert.ThrowsExactlyAsync<TimeoutException>(() => host.Control.AdvanceAsync(TimeSpan.FromSeconds(1)).WaitAsync(Timeout));
            peer.Release();

            // Assert
            StringAssert.Contains(refusal.Message, "Modbus TCP ReadHoldingRegistersAsShort");
            StringAssert.Contains(refusal.Message, "never held");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-018.2")]
        public async Task RecordServedRequestFromClientInSameHostBeforeClockNextAdvances()
        {
            // Arrange — the fetcher requests the serving block's own server, and the serving block counts what its server
            // recorded at t = 1 s, the first instant the clock moves to after the request.
            var port = FreePort();
            await using var host = SteppedHost(builder => builder.AddLogicBlock<HttpServingBlock>("server").AddLogicBlock<HttpFetcherBlock>("fetcher"));
            await host.StartAsync();
            await host.Control.SetPropertyAsync("server", "Port", port);
            await host.Control.SetPropertyAsync("fetcher", "Url", $"http://127.0.0.1:{port}/value");

            // Act
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1)).WaitAsync(Timeout);

            // Assert
            Assert.AreEqual(42, (int)host.Control.GetProperty("fetcher", "Fetched")!);
            Assert.AreEqual(1, (int)host.Control.GetProperty("server", "Recorded")!);
        }

        private static IDevHost SteppedHost(Func<DevConfigurationBuilder, DevConfigurationBuilder> blocks, TimeSpan? quiescence = null)
        {
            var configuration = blocks(DevConfigurationBuilder.Create().WithTopologyName("sockets")).Build();
            var builder = DevHostBuilder.Create()
                                        .WithDi<TestDependencyInjection>()
                                        .WithConfiguration(configuration)
                                        .ConfigureServices(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(Epoch)));
            if (quiescence is { } budget)
            {
                builder = builder.WithSafetyBudgets(new DevHostBudgets { Quiescence = budget });
            }

            return builder.Build();
        }

        private static int FreePort()
        {
            // OS-assigned free port, released for the serving block to bind — the ContractPairingShould precedent.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }
    }
}