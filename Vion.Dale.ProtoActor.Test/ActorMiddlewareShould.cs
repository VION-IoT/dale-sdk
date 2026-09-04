using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.ProtoActor.Test.TestHelpers;
using Vion.Dale.Sdk;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using IActorSystem = Vion.Dale.Sdk.Abstractions.IActorSystem;

namespace Vion.Dale.ProtoActor.Test
{
    /// <summary>
    ///     What the pipeline does around every message it delivers: the two observer notifications, the
    ///     containment of a handler that throws, and the in-flight bracket deterministic stepping rests on.
    ///     Each is driven over a real actor system, because the claim in every case is about a message that
    ///     was actually delivered.
    ///     <para>
    ///         Delivery is awaited on a signal the receiver or the observer raises, never on a sleep
    ///         (<c>testing-conventions.md</c> section 16).
    ///     </para>
    /// </summary>
    [TestClass]
    public sealed class ActorMiddlewareShould
    {
        private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.1")]
        public async Task NotifyObserverBeforeDispatchAndAfterHandling()
        {
            // Arrange
            var observer = new RecordingObserver();
            await using var host = new ObservedHost(observer);
            host.System.CreateRootActorFromDi<RecordingReceiver>("notified");

            // Act
            host.System.SendTo(host.System.LookupByName("notified"), new StopLogicBlockRequest());
            await WaitForHandledAsync<StopLogicBlockRequest>(observer);

            // Assert
            Assert.IsTrue(observer.Received.Any(entry => entry.Message is StopLogicBlockRequest), "The observer sees the message before it is dispatched.");
            var handled = observer.HandledMessages.Single(entry => entry.Message is StopLogicBlockRequest);
            Assert.IsNull(handled.Exception, "A handler that returned is reported with no exception.");
            Assert.AreEqual("notified", handled.ActorName, "Each notification names the actor by its registered identifier.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.1")]
        [TestProperty("spec", "AC-LIFE-014.2")]
        public async Task ReportHandlerThatThrewAndLeaveActorRunning()
        {
            // Arrange
            var observer = new RecordingObserver();
            await using var host = new ObservedHost(observer);
            var actor = host.System.CreateRootActorFromDi<ThrowingReceiver>("throwing");

            // Act
            // Each wait names the message it is waiting for. The observer's semaphore counts handled
            // messages without naming one, so a bare wait was satisfied by the actor's own start-up traffic
            // and the assertion below read the queue one message early.
            host.System.SendTo(actor, new StopLogicBlockRequest());
            await WaitForHandledAsync<StopLogicBlockRequest>(observer);
            host.System.SendTo(actor, new StartLogicBlockRequest());
            await WaitForHandledAsync<StartLogicBlockRequest>(observer);

            // Assert
            Assert.HasCount(2,
                            observer.HandledMessages.Where(entry => entry.Exception is not null),
                            "Every handler exception is reported, which is the only trace the swallow leaves.");
            Assert.IsNotEmpty(host.System.FindByName(new System.Text.RegularExpressions.Regex("^throwing$")),
                              "And the actor is still there afterwards, so one bad block cannot take the network down.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.3")]
        public async Task DeliverMessageWhenObserverThrows()
        {
            // Arrange
            var receiver = new RecordingReceiver();
            await using var host = new ObservedHost(new FaultyObserver(), receiver);
            var actor = host.System.CreateRootActorFor(() => receiver, "faulty_observer");

            // Act
            host.System.SendTo(actor, new StopLogicBlockRequest());
            await receiver.Delivered.WaitAsync(Generous);

            // Assert
            Assert.IsNotEmpty(receiver.Received, "An observer that throws in both of its methods must not cost the message its delivery.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.4")]
        public async Task ReturnInFlightToNothingWhenHandlerReturns()
        {
            // Arrange — one actor and one message per host, so the assertion is not read while another
            // actor's traffic is still in flight. The observer's report is not the point at which the
            // bracket has closed (see WaitForQuietAsync), so both waits are needed and both are signals.
            var monitor = new CountingActivityMonitor();
            var observer = new RecordingObserver();
            await using var host = new ObservedHost(observer, monitor: monitor);
            var actor = host.System.CreateRootActorFromDi<RecordingReceiver>("bracket_returning");

            // Act
            host.System.SendTo(actor, new StopLogicBlockRequest());
            await WaitForHandledAsync<StopLogicBlockRequest>(observer);
            await WaitForQuietAsync(monitor);

            // Assert
            Assert.AreEqual(0L, monitor.InFlight, "In-flight returns to what it was, which is what makes the quiescence barrier exact.");
            Assert.IsGreaterThan(0, Volatile.Read(ref monitor.Left), "The bracket was entered and left rather than never entered at all.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.4")]
        public async Task ReturnInFlightToNothingWhenHandlerThrows()
        {
            // Arrange
            var monitor = new CountingActivityMonitor();
            var observer = new RecordingObserver();
            await using var host = new ObservedHost(observer, monitor: monitor);
            var actor = host.System.CreateRootActorFromDi<ThrowingReceiver>("bracket_throwing");

            // Act
            host.System.SendTo(actor, new StopLogicBlockRequest());
            await WaitForHandledAsync<StopLogicBlockRequest>(observer);
            await WaitForQuietAsync(monitor);

            // Assert
            Assert.AreEqual(0L, monitor.InFlight, "The exit runs on the swallowed-exception path too; a bracket left open there would stall every later quiescence wait.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.3")]
        public async Task DeliverMessageWhenActivityMonitorThrows()
        {
            // Arrange
            var receiver = new RecordingReceiver();
            await using var host = new ObservedHost(monitor: new FaultyActivityMonitor());
            var actor = host.System.CreateRootActorFor(() => receiver, "faulty_monitor");

            // Act
            host.System.SendTo(actor, new StopLogicBlockRequest());
            await receiver.Delivered.WaitAsync(Generous);

            // Assert
            Assert.IsNotEmpty(receiver.Received, "A monitor that throws on entry and on exit must not cost the message its delivery either.");
        }

        /// <summary>
        ///     Waits until the observer has reported the named domain message. The semaphore counts every
        ///     handled message, so waiting on a release alone waits for whichever message arrived first.
        /// </summary>
        private static async Task WaitForHandledAsync<TMessage>(RecordingObserver observer)
        {
            while (!observer.HandledMessages.Any(entry => entry.Message is TMessage))
            {
                await observer.Handled.WaitAsync(Generous);
            }
        }

        /// <summary>
        ///     Waits until no handler is in flight. The observer's report is <em>not</em> that point: the
        ///     pipeline notifies the observer from inside the handler's own try and leaves the bracket in the
        ///     finally after it, so the bracket is still open when the observer's signal arrives. Waiting on
        ///     the monitor's own exit is the synchronisation point; a handler that is in flight is one whose
        ///     exit is still to come, so the loop always terminates.
        /// </summary>
        private static async Task WaitForQuietAsync(CountingActivityMonitor monitor)
        {
            while (monitor.InFlight != 0)
            {
                await monitor.Exited.WaitAsync(Generous);
            }
        }

        private sealed class RecordingObserver : IActorMessageObserver
        {
            public ConcurrentQueue<(string ActorName, object Message)> Received { get; } = new();

            public ConcurrentQueue<(string ActorName, object Message, TimeSpan Elapsed, Exception? Exception)> HandledMessages { get; } = new();

            public SemaphoreSlim Handled { get; } = new(0);

            public void OnReceived(string actorName, object message)
            {
                Received.Enqueue((actorName, message));
            }

            public void OnHandled(string actorName, object message, TimeSpan elapsed, Exception? exception)
            {
                HandledMessages.Enqueue((actorName, message, elapsed, exception));
                Handled.Release();
            }
        }

        private sealed class FaultyObserver : IActorMessageObserver
        {
            public void OnReceived(string actorName, object message)
            {
                throw new InvalidOperationException("the observer refused the notification");
            }

            public void OnHandled(string actorName, object message, TimeSpan elapsed, Exception? exception)
            {
                throw new InvalidOperationException("the observer refused the notification");
            }
        }

        private sealed class CountingActivityMonitor : IActorActivityMonitor
        {
            public int Entered;

            public int Left;

            public SemaphoreSlim Exited { get; } = new(0);

            public long InFlight
            {
                get => Volatile.Read(ref Entered) - Volatile.Read(ref Left);
            }

            public void EnterHandler()
            {
                Interlocked.Increment(ref Entered);
            }

            public void ExitHandler()
            {
                Interlocked.Increment(ref Left);
                Exited.Release();
            }
        }

        private sealed class FaultyActivityMonitor : IActorActivityMonitor
        {
            public long InFlight
            {
                get => 0;
            }

            public void EnterHandler()
            {
                throw new InvalidOperationException("the monitor refused to be entered");
            }

            public void ExitHandler()
            {
                throw new InvalidOperationException("the monitor refused to be left");
            }
        }

        /// <summary>A host with the seams a test chose registered, and nothing else.</summary>
        private sealed class ObservedHost : IAsyncDisposable
        {
            public ServiceProvider Provider { get; }

            public IActorSystem System { get; }

            public ObservedHost(IActorMessageObserver? observer = null, RecordingReceiver? receiver = null, IActorActivityMonitor? monitor = null)
            {
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
                services.AddDaleSdk();
                services.AddProtoActorSystem();
                services.AddTransient<RecordingReceiver>();
                services.AddTransient<ThrowingReceiver>();

                if (observer is not null)
                {
                    services.AddSingleton(observer);
                }

                if (monitor is not null)
                {
                    services.AddSingleton(monitor);
                }

                if (receiver is not null)
                {
                    services.AddSingleton(receiver);
                }

                Provider = services.BuildServiceProvider();
                System = Provider.GetRequiredService<IActorSystem>();
            }

            public ValueTask DisposeAsync()
            {
                return Provider.DisposeAsync();
            }
        }
    }
}