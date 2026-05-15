using System;
using Vion.Dale.Sdk.CodeGeneration;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a message as a state update.
    ///     The message is sent to all linked interfaces.
    ///     The receiving side will get the identifier of the sender
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Struct)]
    public class StateUpdateAttribute : Attribute, IFromToAttribute
    {
        public required string From { get; init; }

        public required string To { get; init; }
    }
}