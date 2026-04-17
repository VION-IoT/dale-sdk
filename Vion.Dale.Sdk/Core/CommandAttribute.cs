using System;
using Vion.Dale.Sdk.CodeGeneration;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a message as a command.
    ///     The message is sent to a specific linked interface instance.
    ///     The receiving side will not get the identifier of the sender.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Struct)]
    public class CommandAttribute : Attribute, IFromToAttribute
    {
        public required string From { get; init; }

        public required string To { get; init; }
    }
}