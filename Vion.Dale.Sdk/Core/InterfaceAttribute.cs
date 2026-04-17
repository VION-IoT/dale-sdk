using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declare interface configuration when implementing a function interface. Allows to set some annotations with the
    ///     optional parameters.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
    public class InterfaceAttribute : Attribute
    {
        public string? Identifier { get; }

        public string? DefaultName { get; }

        public string[] Tags { get; }

        public Type? ForInterface { get; }

        /// <summary>
        ///     Constructor for class-level usage with specific interface targeting.
        /// </summary>
        public InterfaceAttribute(Type forInterface, string? identifier = null, string? defaultName = null, params string[] tags)
        {
            ForInterface = forInterface;
            Identifier = identifier;
            DefaultName = defaultName;
            Tags = tags;
        }
    }
}