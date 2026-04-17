using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declare a dependency on an interface, meaning that the logic block requires an implementation of the specified
    ///     interface. Can be applied to properties (legacy) or classes (new approach).
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)] // todo: remove AttributeTargets.Property
    public class InterfaceDependencyAttribute : Attribute
    {
        public string? DefaultName { get; }

        public CardinalityType Cardinality { get; }

        public SharingType Sharing { get; }

        public DependencyCreationType CreationType { get; }

        public string[] Tags { get; }

        public Type? ForInterface { get; }

        // todo: remove, legacy constructor for property-based usage
        public InterfaceDependencyAttribute(string? defaultName = null,
                                            CardinalityType cardinality = CardinalityType.Mandatory,
                                            SharingType sharing = SharingType.Shared,
                                            DependencyCreationType creationType = DependencyCreationType.MustExist,
                                            params string[] tags)
        {
            DefaultName = defaultName;
            Cardinality = cardinality;
            Sharing = sharing;
            CreationType = creationType;
            Tags = tags;
        }

        /// <summary>
        ///     Constructor for class-level usage with specific interface targeting.
        /// </summary>
        public InterfaceDependencyAttribute(Type forInterface,
                                            string? defaultName = null,
                                            CardinalityType cardinality = CardinalityType.Mandatory,
                                            SharingType sharing = SharingType.Shared,
                                            DependencyCreationType creationType = DependencyCreationType.MustExist,
                                            params string[] tags)
        {
            ForInterface = forInterface;
            DefaultName = defaultName;
            Cardinality = cardinality;
            Sharing = sharing;
            CreationType = creationType;
            Tags = tags;
        }
    }
}