using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Proto;

namespace Vion.Dale.ProtoActor
{
    public class ActorSystem : IActorSystem
    {
        private readonly Proto.ActorSystem _actorSystem;

        private readonly ILogger<ActorSystem> _logger;

        private readonly IServiceProvider _serviceProvider;

        public ActorSystem(IServiceProvider serviceProvider, ILogger<ActorSystem> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
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

            // spawn a temporary actor to handle the acknowledgements
            _actorSystem.Root.Spawn(Props.FromFunc(ctx =>
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

                                                               ctx.ReenterAfter(Task.Delay(timeout),
                                                                                _ =>
                                                                                {
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
                                                           case TAcknowledgementMessage ack: // handle acknowledgement messages, cleanup after last message
                                                           {
                                                               // Map the sender PID back to the original actor reference
                                                               if (pidToActorMap.TryGetValue(ctx.Sender!, out var actorRef))
                                                               {
                                                                   responses[actorRef] = ack;
                                                               }

                                                               remainingCount--;
                                                               if (remainingCount == 0)
                                                               {
                                                                   tcs.TrySetResult(responses);
                                                                   ctx.Stop(ctx.Self);
                                                               }

                                                               break;
                                                           }
                                                       }

                                                       return Task.CompletedTask;
                                                   }));

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

            var genericActor = ActivatorUtilities.CreateInstance(_serviceProvider, actorType, actorReceiverInstance);
            if (genericActor == null)
            {
                throw new InvalidOperationException($"Actor type {actorType.FullName} is not registered in the service provider.");
            }

            var pid = _actorSystem.Root.SpawnNamed(Props.FromProducer(() => (IActor)genericActor)
                                                        .WithReceiverMiddleware(ActorMiddleware.ReceiveMiddleware(logger ?? _logger))
                                                        .WithSenderMiddleware(ActorMiddleware.SenderMiddleware(logger ?? _logger)),
                                                   name);
            return pid.ToActorReference();
        }

        /// <inheritdoc />
        public IActorReference CreateRootActorFor<TActorReceiver>(Func<TActorReceiver> factory, string name, object? logger)
            where TActorReceiver : IActorReceiver
        {
            var pid = _actorSystem.Root.SpawnNamed(Props.FromProducer(() => new Actor<TActorReceiver>(factory()))
                                                        .WithReceiverMiddleware(ActorMiddleware.ReceiveMiddleware(logger as ILogger ?? _logger))
                                                        .WithSenderMiddleware(ActorMiddleware.SenderMiddleware(logger as ILogger ?? _logger)),
                                                   name);
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

            _actorSystem.Root.Spawn(Props.FromFunc(ctx =>
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

                                                               ctx.ReenterAfter(Task.Delay(timeout),
                                                                                _ =>
                                                                                {
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
                                                                   tcs.TrySetResult();
                                                                   ctx.Stop(ctx.Self); // Cleanup
                                                               }

                                                               break;
                                                           }
                                                       }

                                                       return Task.CompletedTask;
                                                   }));

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
    }
}