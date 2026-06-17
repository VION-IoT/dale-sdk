using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Proto;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Mqtt;

namespace Vion.Dale.ProtoActor
{
    public static class ActorMiddleware
    {
        public static Func<Receiver, Receiver> ReceiveMiddleware(ILogger logger,
                                                                 IActorMessageObserver? observer = null,
                                                                 TimeProvider? timeProvider = null,
                                                                 IActorActivityMonitor? activityMonitor = null)
        {
            var clock = timeProvider ?? TimeProvider.System;
            return next => async (context, envelope) =>
                           {
                               var actorName = context.Self?.Id ?? string.Empty;

                               // Optional, opt-in tap (RFC 0003): only active when an observer is registered
                               // (DevHost). Null in production → behaviour is unchanged.
                               if (observer != null && envelope.Message is not null)
                               {
                                   try
                                   {
                                       observer.OnReceived(actorName, envelope.Message);
                                   }
                                   catch
                                   {
                                       // A faulty observer must never affect message delivery.
                                   }
                               }

                               // Optional, opt-in in-flight bracket (RFC 0003 — deterministic stepping): only
                               // active when a monitor is registered (DevHost). Entered BEFORE the handler runs
                               // so any follow-up the handler posts happens while in-flight is already > 0 — the
                               // invariant that makes the quiescence barrier exact. Null in production →
                               // behaviour is unchanged. A faulty monitor must never affect delivery.
                               if (activityMonitor != null)
                               {
                                   try
                                   {
                                       activityMonitor.EnterHandler();
                                   }
                                   catch
                                   {
                                       // A faulty monitor must never affect message delivery.
                                   }
                               }

                               try
                               {
                                   if (logger.IsEnabled(LogLevel.Trace))
                                   {
                                       logger.LogDebug("[RECEIVE] Message: {Message}, Headers: {Headers}",
                                                       GetFriendlyTypeName(envelope.Message!.GetType()),
                                                       SerializeHeaders(envelope.Header));
                                   }

                                   // RFC 0005: time the handler and report the outcome (including the swallowed
                                   // exception) to the vitals collector via OnHandled, preserving the existing
                                   // log-and-swallow below.
                                   await ObservedHandler.RunAsync(observer, actorName, envelope.Message, clock, () => next(context, envelope));
                               }
                               catch (Exception ex)
                               {
                                   var serializedHeaders = SerializeHeaders(envelope.Header);
                                   var messageTypeName = GetFriendlyTypeName(envelope.Message!.GetType());
                                   if (messageTypeName != nameof(MqttMessageReceived))
                                   {
                                       logger.LogError(ex, "[EXCEPTION CAUGHT] Message: {Message}, Headers: {Headers}", messageTypeName, serializedHeaders);
                                   }
                                   else
                                   {
                                       var message = (MqttMessageReceived)envelope.Message!;
                                       var correlationId = message.TryGetCorrelationId();
                                       logger.LogError(ex,
                                                       "[EXCEPTION CAUGHT] Message: {Message}, Headers: {Headers} (CorrelationId={CorrelationId}, Topic={Topic})",
                                                       messageTypeName,
                                                       serializedHeaders,
                                                       correlationId,
                                                       message.Topic);
                                   }
                               }
                               finally
                               {
                                   // Mirror of EnterHandler — runs on both the success path and the swallowed-
                                   // exception path above, so in-flight always returns to its pre-handler value.
                                   if (activityMonitor != null)
                                   {
                                       try
                                       {
                                           activityMonitor.ExitHandler();
                                       }
                                       catch
                                       {
                                           // A faulty monitor must never affect message delivery.
                                       }
                                   }
                               }
                           };
        }

        public static Func<Sender, Sender> SenderMiddleware(ILogger logger)
        {
            return next => (context, target, envelope) =>
                           {
                               if (logger.IsEnabled(LogLevel.Trace))
                               {
                                   logger.LogTrace("[SEND] To: {Target}, Message: {Message}, Headers: {Headers}",
                                                   target,
                                                   GetFriendlyTypeName(envelope.Message.GetType()),
                                                   SerializeHeaders(envelope.Header));
                               }

                               return next(context, target, envelope);
                           };
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.Name;
            }

            var typeName = type.Name[..type.Name.IndexOf('`')];
            var genericArgs = type.GetGenericArguments();
            var argNames = string.Join(", ", genericArgs.Select(GetFriendlyTypeName));
            return $"{typeName}<{argNames}>";
        }

        private static string SerializeHeaders(MessageHeader? headers)
        {
            if (headers == null || headers.Count == 0)
            {
                return "{}";
            }

            return string.Join(", ", headers);
        }
    }
}