using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a message as a request message.
    ///     The message is sent to a specific linked interface instance.
    ///     The receiving/responding side will need to return the response message.
    ///     The responding side will not get the identifier of the sender.
    ///     The requesting side will receive the identifier of the responder with the response.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Struct)]
    public class RequestResponseAttribute : Attribute
    {
        public required string From { get; init; }

        public required string To { get; init; }

        public required Type ResponseType { get; init; }
    }
}