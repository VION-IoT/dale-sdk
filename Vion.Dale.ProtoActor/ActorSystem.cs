using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Mailbox;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.ProtoActor
{
    public class ActorSystem : IActorSystem
    {
        // Optional, opt-in in-flight handler monitor (DevHost's deterministic-stepping barrier — RFC 0003).
        // Null when none is registered, so a host without it keeps the original behaviour.
        private readonly IActorActivityMonitor? _activityMonitor;

        private readonly Proto.ActorSystem _actorSystem;

        // Optional, opt-in pause gate for delayed self-sends (DevHost's pause feature). Null when none is
        // registered, so a host without it keeps the original scheduling behaviour.
        private readonly IDelayedSendGate? _delayedSendGate;

        // RFC 0008 deterministic stepping: on a stepped host (a controllable FakeTimeProvider clock) every
        // actor is spawned onto one shared serial dispatcher so the message cascade within a quiescence round
        // drains in a single reproducible order instead of fanning out across the thread pool. Null on a
        // real-clock host (free-running Player / production), so Proto's ThreadPoolDispatcher is used as before.
        private readonly IDispatcher? _deterministicDispatcher;

        private readonly ILogger<ActorSystem> _logger;

        // Optional, opt-in message observers (DevHost's tap — RFC 0003 — and the vitals collector — RFC 0005),
        // combined into the single middleware slot. Null when none is registered, so a host that registers no
        // observer keeps the original behaviour.
        private readonly IActorMessageObserver? _messageObserver;

        // Optional, opt-in virtual schedule of pending delayed sends (DevHost's next-event stepping — RFC
        // 0003). Null when none is registered, so a host without it keeps the original behaviour. Threaded
        // into every spawned actor's context, and used here for the two internal ack/stop timeout waits.
        private readonly IVirtualSchedule? _schedule;

        private readonly IServiceProvider _serviceProvider;

        // Drives handler-duration measurement in the middleware: the real clock in production, a
        // FakeTimeProvider under test. Defaults to the system clock when none is registered.
        private readonly TimeProvider _timeProvider;

        // RFC 0005: the vitals core's spawn-time write surface. Null when the core isn't registered
        // (a bare host, or the TestKit with vitals off), leaving spawn behaviour unchanged.
        private readonly IActorVitalsCollector? _vitalsCollector;

        public ActorSystem(IServiceProvider serviceProvider, ILogger<ActorSystem> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _messageObserver = CompositeActorMessageObserver.Combine(serviceProvider.GetServices<IActorMessageObserver>());
            _delayedSendGate = serviceProvider.GetService<IDelayedSendGate>();
            _activityMonitor = serviceProvider.GetService<IActorActivityMonitor>();
            _schedule = serviceProvider.GetService<IVirtualSchedule>();
            _timeProvider = serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
            _vitalsCollector = serviceProvider.GetService<IActorVitalsCollector>();

            // A controllable (FakeTimeProvider) clock means the host is stepped (same structural detection as
            // DeterministicStepper / IDevHostControl.IsStepped). Build one shared exclusive scheduler and wrap
            // it in the serial dispatcher applied to every actor below — created once per actor system so all
            // actors serialize onto the SAME timeline.
            if (IsSteppedClock(_timeProvider))
            {
                _deterministicDispatcher = new DeterministicDispatcher(new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler);
            }

            _actorSystem = serviceProvider.GetService<Proto.ActorSystem>() ?? throw new InvalidOperationException("Actor system is not registered in the service provider.");
            _actorSystem.EventStream.Subscribe<DeadLetterEvent>(e =>
                                                                {
                                                                    if (e.Message is not Continuation) // Ignore continuation messages from ctx.ReenterAfter() timeout checking
                                                                    {
                                                                        logger.LogWarning("DeadLetter: message {Message} from {Sender} to {PID}", e.Message, e.Sender, e.Pid);
                                                                    }
                                                                });
        }

        /// <inheritdoc />
        public void SendTo<TMessage>(IActorReference target, TMessage message)
            where TMessage : struct
        {
            var targetPid = ((ActorReference)target).Pid;
            _actorSystem.Root.Send(targetPid, message);
        }

        /// <inheritdoc />
        public Task<Dictionary<IActorReference, TAcknowledgementMessage>> SendAndWaitForAcknowledgementAsync<TRequestMessage, TAcknowledgementMessage>(
            List<IActorReference> actors,
            TRequestMessage message,
            TimeSpan timeout)
            where TRequestMessage : struct
            where TAcknowledgementMessage : struct
        {
            var actorMessages = actors.ToDictionary(actor => actor, _ => message);
            return SendAndWaitForAcknowledgementAsync<TRequestMessage, TAcknowledgementMessage>(actorMessages, timeout);
        }

        /// <inheritdoc />
        public async Task<Dictionary<IActorReference, TAcknowledgementMessage>> SendAndWaitForAcknowledgementAsync<TRequestMessage, TAcknowledgementMessage>(
            Dictionary<IActorReference, TRequestMessage> actorMessages,
            TimeSpan timeout)
            where TRequestMessage : struct
            where TAcknowledgementMessage : struct
        {
            if (actorMessages.Count == 0)
            {
                return new Dictionary<IActorReference, TAcknowledgementMessage>();
            }

            var startTime = Stopwatch.GetTimestamp();
            var tcs = new TaskCompletionSource<Dictionary<IActorReference, TAcknowledgementMessage>>();
            var remainingCount = actorMessages.Count;
            var responses = new Dictionary<IActorReference, TAcknowledgementMessage>();
            var pidToActorMap = actorMessages.Keys.ToDictionary(actorRef => ((ActorReference)actorRef).Pid, actorRef => actorRef);

            // This timeout wait is a delayed send too — register its virtual due-time so next-event stepping
            // can see it (else a stepped run could skip past its due-time). Per-call token; unregistered both
            // in the timeout continuation AND on the normal-completion path (last ack), so a fast ack — the
            // usual case — does not leak a far-future entry that the stepper would otherwise hop to.
            var timeoutToken = new object();

            // spawn a temporary actor to handle the acknowledgements
            _actorSystem.Root.Spawn(WithDeterministicDispatcher(Props.FromFunc(ctx =>
                                                                               {
                                                                                   switch (ctx.Message)
                                                                                   {
                                                                                       case Started: // Send messages to all actors and set up timeout
                                                                                       {
                                                                                           foreach (var (actorRef, msg) in actorMessages)
                                                                                           {
                                                                                               var pid = ((ActorReference)actorRef).Pid;
                                                                                               ctx.Request(pid, msg);
                                                                                           }

                                                                                           _schedule?.Register(timeoutToken, _timeProvider.GetUtcNow() + timeout);
                                                                                           ctx.ReenterAfter(Task.Delay(timeout, _timeProvider),
                                                                                                            _ =>
                                                                                                            {
                                                                                                                _schedule?.Unregister(timeoutToken);
                                                                                                                if (remainingCount > 0)
                                                                                                                {
                                                                                                                    var elapsed = Stopwatch.GetElapsedTime(startTime);
                                                                                                                    _logger
                                                                                                                        .LogWarning("Timeout waiting for {RemainingCount} acknowledgements after {ElapsedMs}ms",
                                                                                                                            remainingCount,
                                                                                                                            elapsed.TotalMilliseconds);
                                                                                                                    tcs.TrySetException(new
                                                                                                                        TimeoutException($"Timeout waiting for {remainingCount} actor(s) to acknowledge"));
                                                                                                                    ctx.Stop(ctx.Self);
                                                                                                                }

                                                                                                                return Task.CompletedTask;
                                                                                                            });
                                                                                           break;
                                                                                       }
                                                                                       case TAcknowledgementMessage ack
                                                                                           : // handle acknowledgement messages, cleanup after last message
                                                                                       {
                                                                                           // Map the sender PID back to the original actor reference
                                                                                           if (pidToActorMap.TryGetValue(ctx.Sender!, out var actorRef))
                                                                                           {
                                                                                               responses[actorRef] = ack;
                                                                                           }

                                                                                           remainingCount--;
                                                                                           if (remainingCount == 0)
                                                                                           {
                                                                                               _schedule?.Unregister(timeoutToken);
                                                                                               tcs.TrySetResult(responses);
                                                                                               ctx.Stop(ctx.Self);
                                                                                           }

                                                                                           break;
                                                                                       }
                                                                                   }

                                                                                   return Task.CompletedTask;
                                                                               })));

            var result = await tcs.Task;
            var totalElapsed = Stopwatch.GetElapsedTime(startTime);
            _logger.LogDebug("SendAndWaitForAcknowledgementAsync completed in {ElapsedMs}ms for {ActorCount} actor(s)", totalElapsed.TotalMilliseconds, actorMessages.Count);
            return result;
        }

        /// <inheritdoc />
        public IActorReference CreateRootActorFromDi<T>(string name, ILogger? logger = null)
        {
            return CreateRootActorFromDi(typeof(T), name, logger);
        }

        /// <inheritdoc />
        public IActorReference CreateRootActorFromDi(Type actorReceiverType, string name, ILogger? logger = null)
        {
            var actorType = typeof(Actor<>).MakeGenericType(actorReceiverType);
            var constructorTakesLogger = actorReceiverType.GetConstructors()
                                                          .SelectMany(c => c.GetParameters())
                                                          .Any(p => p.ParameterType == typeof(ILogger) ||
                                                                    (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>)));

            object actorReceiverInstance;
            if (!constructorTakesLogger || logger == null)
            {
                actorReceiverInstance = ActivatorUtilities.CreateInstance(_serviceProvider, actorReceiverType);
            }
            else
            {
                // Determine if the constructor expects ILogger<T> (generic) rather than ILogger (non-generic).
                // If so, wrap the plain ILogger in a Logger<T> so ActivatorUtilities can match the parameter type.
                var loggerParam = actorReceiverType.GetConstructors()
                                                   .SelectMany(c => c.GetParameters())
                                                   .FirstOrDefault(p => p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(ILogger<>));

                object loggerArg = logger;
                if (loggerParam != null)
                {
                    var expectedLoggerType = loggerParam.ParameterType;
                    var loggerWrapperType = typeof(Logger<>).MakeGenericType(expectedLoggerType.GetGenericArguments()[0]);
                    loggerArg = Activator.CreateInstance(loggerWrapperType, _serviceProvider.GetRequiredService<ILoggerFactory>())!;
                }

                actorReceiverInstance = ActivatorUtilities.CreateInstance(_serviceProvider, actorReceiverType, loggerArg);
            }

            var genericActor = _delayedSendGate is null ? ActivatorUtilities.CreateInstance(_serviceProvider, actorType, actorReceiverInstance) :
                                   ActivatorUtilities.CreateInstance(_serviceProvider, actorType, actorReceiverInstance, _delayedSendGate);
            if (genericActor == null)
            {
                throw new InvalidOperationException($"Actor type {actorType.FullName} is not registered in the service provider.");
            }

            var props = Props.FromProducer(() => (IActor)genericActor)
                             .WithReceiverMiddleware(ActorMiddleware.ReceiveMiddleware(logger ?? _logger, _messageObserver, _timeProvider, _activityMonitor))
                             .WithSenderMiddleware(ActorMiddleware.SenderMiddleware(logger ?? _logger));
            props = WithVitals(props, actorReceiverType, name);
            props = WithDeterministicDispatcher(props);

            var pid = _actorSystem.Root.SpawnNamed(props, name);
            return pid.ToActorReference();
        }

        /// <inheritdoc />
        public IActorReference CreateRootActorFor<TActorReceiver>(Func<TActorReceiver> factory, string name, object? logger)
            where TActorReceiver : IActorReceiver
        {
            var props = Props.FromProducer(() => new Actor<TActorReceiver>(factory(), _delayedSendGate, _timeProvider, _schedule))
                             .WithReceiverMiddleware(ActorMiddleware.ReceiveMiddleware(logger as ILogger ?? _logger, _messageObserver, _timeProvider, _activityMonitor))
                             .WithSenderMiddleware(ActorMiddleware.SenderMiddleware(logger as ILogger ?? _logger));
            props = WithVitals(props, typeof(TActorReceiver), name);
            props = WithDeterministicDispatcher(props);

            var pid = _actorSystem.Root.SpawnNamed(props, name);
            return pid.ToActorReference();
        }

        /// <inheritdoc />
        public Task StopActorsAndWaitAsync(List<IActorReference> actorsToStop, TimeSpan timeout)
        {
            var pidsToStop = actorsToStop.Select(actorReference => ((ActorReference)actorReference).Pid).ToList();
            if (pidsToStop.Count == 0)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource();
            var remainingCount = pidsToStop.Count;

            // See the ack-wait note above: register the timeout in the virtual schedule and unregister it on
            // BOTH the timeout continuation and the normal-completion path (last Terminated), so a quick
            // termination — the usual case — leaves no far-future entry for the stepper to hop to.
            var timeoutToken = new object();

            _actorSystem.Root.Spawn(WithDeterministicDispatcher(Props.FromFunc(ctx =>
                                                                               {
                                                                                   switch (ctx.Message)
                                                                                   {
                                                                                       case Started: // Start watching all actors and set up timeout
                                                                                       {
                                                                                           _logger.LogDebug("Watching {ActorCount} actors for termination", pidsToStop.Count);

                                                                                           foreach (var pid in pidsToStop)
                                                                                           {
                                                                                               ctx.Watch(pid);
                                                                                           }

                                                                                           _schedule?.Register(timeoutToken, _timeProvider.GetUtcNow() + timeout);
                                                                                           ctx.ReenterAfter(Task.Delay(timeout, _timeProvider),
                                                                                                            _ =>
                                                                                                            {
                                                                                                                _schedule?.Unregister(timeoutToken);
                                                                                                                if (remainingCount > 0)
                                                                                                                {
                                                                                                                    _logger
                                                                                                                        .LogWarning("Timeout waiting for {RemainingCount} actors to terminate after {TimeoutMs}ms",
                                                                                                                            remainingCount,
                                                                                                                            timeout.TotalMilliseconds);
                                                                                                                    tcs.TrySetException(new
                                                                                                                        TimeoutException($"Timeout waiting for {remainingCount} actor(s) to terminate after {timeout.TotalMilliseconds}ms"));
                                                                                                                    ctx.Stop(ctx.Self);
                                                                                                                }

                                                                                                                return Task.CompletedTask;
                                                                                                            });

                                                                                           break;
                                                                                       }
                                                                                       case Terminated: // handle terminated message from watched actors, cleanup after last
                                                                                       {
                                                                                           remainingCount--;
                                                                                           if (remainingCount == 0)
                                                                                           {
                                                                                               _schedule?.Unregister(timeoutToken);
                                                                                               tcs.TrySetResult();
                                                                                               ctx.Stop(ctx.Self); // Cleanup
                                                                                           }

                                                                                           break;
                                                                                       }
                                                                                   }

                                                                                   return Task.CompletedTask;
                                                                               })));

            foreach (var pid in pidsToStop)
            {
                _actorSystem.Root.Stop(pid);
            }

            return tcs.Task;
        }

        /// <inheritdoc />
        public async Task ShutdownAsync()
        {
            _logger.LogInformation("Shutting down actor system...");
            await _actorSystem.ShutdownAsync("Client triggered shutdown");
            _logger.LogInformation("Actor system shutdown completed.");
        }

        /// <inheritdoc />
        public List<IActorReference> FindByName(Regex actorNameRegex)
        {
            var pids = _actorSystem.ProcessRegistry.Find(actorNameRegex.IsMatch).ToList();
            return pids.Select(pid => pid.ToActorReference()).ToList();
        }

        /// <inheritdoc />
        public IActorReference LookupByName(string name)
        {
            return new ActorReference(PidUtils.FromName(name));
        }

        // RFC 0008: on a stepped host, route the actor onto the shared serial dispatcher so its handlers never
        // run concurrently with another actor's. No-op on a real-clock host (keeps Proto's ThreadPoolDispatcher).
        private Props WithDeterministicDispatcher(Props props)
        {
            return _deterministicDispatcher is null ? props : props.WithDispatcher(_deterministicDispatcher);
        }

        // Structural detection of a controllable clock (FakeTimeProvider): a public instance Advance(TimeSpan)
        // returning void. Mirrors DeterministicStepper.BindAdvance / IDevHostControl.IsStepped so the shipped
        // library needs no compile-time reference to the test-only TimeProvider.Testing assembly.
        private static bool IsSteppedClock(TimeProvider timeProvider)
        {
            var advance = timeProvider.GetType().GetMethod("Advance", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(TimeSpan) }, null);
            return advance is { ReturnType: { } returnType } && returnType == typeof(void);
        }

        // RFC 0005: register the actor's identity and attach mailbox-depth statistics when the vitals core
        // is present. No-op otherwise, so spawn behaviour is unchanged for hosts without diagnostics.
        private Props WithVitals(Props props, Type receiverType, string name)
        {
            if (_vitalsCollector == null)
            {
                return props;
            }

            _vitalsCollector.Register(name, ActorIdentity.For(receiverType, name));
            return props.WithMailbox(() => UnboundedMailbox.Create(new VitalsMailboxStatistics(name, _vitalsCollector)));
        }
    }
}