using Proto;

namespace Vion.Dale.ProtoActor.Extensions
{
    public static class ContextExtensions
    {
        /// <summary>
        ///     Sends a message to the specified target PID with the current context's PID as sender and includes the passed or
        ///     current headers.
        /// </summary>
        public static void SendWithHeaders(this IContext context, PID target, object message, MessageHeader? headers = null)
        {
            context.Send(target, new MessageEnvelope(message, context.Self, headers ?? context.Headers));
        }
    }
}