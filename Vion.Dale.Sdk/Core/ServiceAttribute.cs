using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declare a service on a logic block or on a property of a logic block.
    ///     On a logic block, the Service attribute can be omitted (then class name + all implemented service interfaces are
    ///     used)
    ///     On a property the service attribute can be omitted if the property type implements service interfaces.
    ///     Identifier can be empty (then class or property name is used)
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
    public class ServiceAttribute : Attribute
    {
        public string Identifier { get; }

        public ServiceAttribute(string identifier = "")
        {
            Identifier = identifier;
        }
    }
}