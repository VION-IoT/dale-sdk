using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Web;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     DevHost teardown runs the runtime's domain stop sequence — stop acknowledgement, then the
    ///     persistent-data snapshot, then actor termination — so <c>LogicBlockBase.Stopping()</c> is actually
    ///     invoked in development. Before the fix <c>DevHost.StopAsync</c> carried a commented-out TODO and no
    ///     production sender for <see cref="StopLogicBlockRequest" /> existed anywhere in this repo.
    ///     <para>
    ///         Every test here drives a real built host through its own <c>await using</c> / DisposeAsync —
    ///         the seam that was broken. Pushing the message into <c>HandleMessageAsync</c> directly (as
    ///         <c>LogicBlockShutdownGuardShould</c> does) would prove the handler works, which was never in
    ///         doubt, and nothing about teardown.
    ///     </para>
    /// </summary>
    [TestClass]
    public class TeardownStopSequenceShould
    {
        // Both clock modes, because the failure modes differ: free-running teardown waits on the wall clock,
        // while a stepped host's per-wait timeouts are virtual and nothing advances the fake clock during
        // teardown — the mode the real-time backstop exists for (docs/testing-conventions.md § 5).
        [TestMethod]
        [DataRow(false, DisplayName = "free-running clock")]
        [DataRow(true, DisplayName = "stepped clock")]
        public async Task InvokeStoppingOnEveryBlock_WhenTheHostIsDisposed(bool stepped)
        {
            var recorder = new TeardownRecorder();

            await using var host = BuildHost(recorder, stepped);
            await host.StartAsync();

            CollectionAssert.DoesNotContain(recorder.Entries.ToList(), TeardownRecorder.Stopping, "Stopping() must not run while the host is up.");

            await host.DisposeAsync();

            CollectionAssert.Contains(recorder.Entries.ToList(),
                                      TeardownRecorder.Stopping,
                                      "Disposing the host must send the domain stop to the logic blocks, which is what invokes Stopping(). " + "Recorded: " +
                                      string.Join(", ", recorder.Entries));
        }

        [TestMethod]
        [DataRow(false, DisplayName = "free-running clock")]
        [DataRow(true, DisplayName = "stepped clock")]
        public async Task RunStopThenSnapshotThenTerminate_InThatOrder(bool stepped)
        {
            var recorder = new TeardownRecorder();

            await using var host = BuildHost(recorder, stepped);
            await host.StartAsync();
            await host.DisposeAsync();

            var entries = recorder.Entries;
            var stopRequest = IndexOfMessage(entries, nameof(StopLogicBlockRequest));
            var stopping = entries.ToList().IndexOf(TeardownRecorder.Stopping);
            var snapshotRequest = IndexOfMessage(entries, nameof(GetPersistentDataSnapshotRequest));

            // The started block's per-actor DI scope, torn down on its terminal Stopped — i.e. the actor has
            // terminated. ScopeProbe records only for a scope whose block was started, so the instance
            // introspection resolves out of the root provider cannot be mistaken for it.
            var terminated = entries.ToList().IndexOf(TeardownRecorder.ScopeDisposed);

            var recorded = "Recorded: " + string.Join(", ", entries);
            Assert.IsGreaterThanOrEqualTo(0, stopRequest, "The block must receive StopLogicBlockRequest during teardown. " + recorded);
            Assert.IsGreaterThanOrEqualTo(0, snapshotRequest, "The block must receive GetPersistentDataSnapshotRequest during teardown. " + recorded);
            Assert.IsGreaterThanOrEqualTo(0, terminated, "The block actor's DI scope must be disposed, i.e. the actor must be terminated. " + recorded);

            Assert.IsLessThan(stopping, stopRequest, "Stopping() runs from the StopLogicBlockRequest handler, so the request must be recorded first. " + recorded);
            Assert.IsLessThan(snapshotRequest, stopping, "The snapshot request must follow the stop, mirroring the runtime's sequence. " + recorded);
            Assert.IsLessThan(terminated, snapshotRequest, "Both domain messages must be handled before the actors are terminated. " + recorded);
        }

        [TestMethod]
        [DataRow(false, DisplayName = "free-running clock")]
        [DataRow(true, DisplayName = "stepped clock")]
        public async Task DeliverAServicePropertyWriteIssuedFromStopping(bool stepped)
        {
            // The modest half of the third acceptance criterion: an operation enqueued by Stopping() reaches
            // its destination. A service-property write does, because the publish it raises is sent from the
            // same handler and the mock service handler actor outlives the block. A best-effort *contract*
            // write is the case that needs a grace period between Stopping() and teardown — VION-65.
            var recorder = new TeardownRecorder();
            var events = new ConcurrentQueue<DevHostEvent>();

            await using var host = BuildHost(recorder, stepped);
            await host.StartAsync();

            // Subscribed after start and never disposed before the host: the publish under test happens
            // during DisposeAsync, so an unsubscribe ordered by a using-scope would silently drop it.
            host.Control.Subscribe(events.Enqueue);

            await host.DisposeAsync();

            var final = events.OfType<ServicePropertyChanged>().LastOrDefault(e => e.Property == nameof(StoppingBlock.Status));

            Assert.IsNotNull(final, "The Status write issued from Stopping() must be published. Events: " + string.Join(", ", events.Select(e => e.GetType().Name)));
            Assert.AreEqual(StoppingBlock.StoppedStatus, final.Value, "The final published Status must be the value Stopping() wrote.");
        }

        [TestMethod]
        public async Task NotHangOrThrow_WhenTheHostWasNeverStarted()
        {
            // Teardown discovers the block actors from the process registry, so a host that never started has
            // nothing to stop and must fall straight through — not send into dead letters and ride out the
            // stop timeout. 122 `await using … Build()` sites in this project depend on teardown staying cheap.
            var recorder = new TeardownRecorder();
            var host = BuildHost(recorder, false);

            var dispose = host.DisposeAsync().AsTask();

            Assert.AreSame(dispose, await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(10))), "Disposing a never-started host must not wait on a stop acknowledgement.");
            await dispose;
            CollectionAssert.DoesNotContain(recorder.Entries.ToList(), TeardownRecorder.Stopping, "A host that never started has no started blocks to stop.");
        }

        [TestMethod]
        [TestCategory("Smoke")]
        public async Task RunTheStopSequenceOnHostRecycle_NotOnlyOnProcessExit()
        {
            // Acceptance criterion 2. The recycle path is the same seam — DevHostWebRunner's supervised loop
            // is `await using var host` per generation, so ending an iteration runs DisposeAsync → StopAsync —
            // but "follows from the shared seam" is an argument, not a demonstration. Drive a real reset
            // through the HTTP surface the UI's recycle button uses and watch generation 1 stop properly.
            var recorder = new TeardownRecorder();
            var port = FreePort();

            IDevHost Factory(string? requestedTopology)
            {
                return BuildHost(recorder, true, port);
            }

            Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, "1");
            using var cts = new CancellationTokenSource();

            // The supervised loop is stopped in the finally, not after the try: a failing assertion must not
            // leak a running DevHostWebRunner and a bound port into the rest of the test process.
            Task? runner = null;
            try
            {
                runner = DevHostWebRunner.RunAsync(Factory, port, cts.Token);
                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(10) };

                Assert.IsTrue(await PollSteppedReadyAsync(client, TimeSpan.FromSeconds(30)), "Generation 1 should come up.");
                CollectionAssert.DoesNotContain(recorder.Entries.ToList(), TeardownRecorder.Stopping, "Nothing has been recycled yet.");

                var reset = await client.PostAsync("/api/control/reset", null);
                Assert.AreEqual(HttpStatusCode.Accepted, reset.StatusCode, "The recycle is accepted and runs asynchronously — the supervised loop rebuilds the generation.");

                // Generation 2 answering means generation 1 completed its teardown — the recycle did not hang.
                Assert.IsTrue(await PollSteppedReadyAsync(client, TimeSpan.FromSeconds(30)), "Generation 2 should come up after the recycle.");
                CollectionAssert.Contains(recorder.Entries.ToList(),
                                          TeardownRecorder.Stopping,
                                          "The recycled generation's blocks must have been stopped. Recorded: " + string.Join(", ", recorder.Entries));
            }
            finally
            {
                Environment.SetEnvironmentVariable(DevHostWebRunner.NoBrowserEnvVar, null);
                cts.Cancel();

                if (runner is not null)
                {
                    await runner;
                }
            }
        }

        private static async Task<bool> PollSteppedReadyAsync(HttpClient client, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var status = JsonDocument.Parse(await client.GetStringAsync("/api/control/status")).RootElement;
                    if (status.GetProperty("stepped").GetBoolean() && status.GetProperty("canReset").GetBoolean())
                    {
                        return true;
                    }
                }
                catch
                {
                    // Host (re)starting — keep polling.
                }

                await Task.Delay(250);
            }

            return false;
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static int IndexOfMessage(IReadOnlyList<string> entries, string messageTypeName)
        {
            return entries.ToList().FindIndex(entry => entry.StartsWith(messageTypeName + "@", StringComparison.Ordinal));
        }

        private static IDevHost BuildHost(TeardownRecorder recorder, bool stepped, int? webUiPort = null)
        {
            var configuration = DevConfigurationBuilder.Create().WithTopologyName("stopper").AddLogicBlock<StoppingBlock>("stopper").Build();

            var builder = DevHostBuilder.Create()
                                        .WithDi<TestDependencyInjection>()
                                        .WithConfiguration(configuration)
                                        .ConfigureServices(services =>
                                                           {
                                                               services.AddTransient<StoppingBlock>();
                                                               services.AddScoped<ScopeProbe>();

                                                               // The recorder is the test's own instance, registered as a singleton so the
                                                               // per-block scope resolves the same object the assertions read.
                                                               services.AddSingleton(recorder);
                                                               services.AddSingleton<IActorMessageObserver>(recorder);
                                                           });

            if (stepped)
            {
                builder = builder.WithDeterministicStepping();
            }

            if (webUiPort is { } port)
            {
                builder = builder.WithWebUi(port);
            }

            return builder.Build();
        }
    }
}