using System;
using System.Linq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Mqtt;
using Microsoft.Extensions.Logging;
using Proto;

namespace Vion.Dale.ProtoActor
{
    public static class ActorMiddleware
    {
        public static Func<Receiver, Receiver> ReceiveMiddleware(ILogger logger, IActorMessageObserver? observer = null)
        {
            return next => async (context, envelope) =>
                           {
                               // Optional, opt-in tap (RFC 0003): only active when an observer is registered
                               // (DevHost). Null in production → behaviour is unchanged.
                               if (observer != null && envelope.Message is not null)
                               {
                                   try
                                   {
                                       observer.OnReceived(context.Self?.Id ?? string.Empty, envelope.Message);
                                   }
                                   catch
                                   {
                                       // A faulty observer must never affect message delivery.
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

                                   await next(context, envelope);
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