using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declares a service provider contract on a logic block property with optional metadata.
    ///     If no identifier is provided, the property name will be used.
    ///     The contract type is automatically determined from the property type.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class ServiceProviderContractAttribute : Attribute
    {
        public string? Identifier { get; }

        public string? DefaultName { get; }

        public CardinalityType Cardinality { get; }

        public SharingType Sharing { get; }

        public string[] Tags { get; }

        public ServiceProviderContractAttribute(string? identifier = null,
                                                string? defaultName = null,
                                                CardinalityType cardinality = CardinalityType.Mandatory,
                                                SharingType sharing = SharingType.Shared,
                                                params string[] tags)
        {
            Identifier = identifier;
            DefaultName = defaultName;
            Cardinality = cardinality;
            Sharing = sharing;
            Tags = tags;
        }
    }
}