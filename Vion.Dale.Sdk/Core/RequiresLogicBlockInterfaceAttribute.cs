using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declares that the LogicBlock requires an implementation of the specified
    ///     interface to be linked at runtime. The LB doesn't implement the interface
    ///     itself; instead, an instance must be wired in for the LB to function.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class RequiresLogicBlockInterfaceAttribute : Attribute
    {
        public Type ForInterface { get; }

        public string? DefaultName { get; init; }

        public CardinalityType Cardinality { get; init; } = CardinalityType.Mandatory;

        public SharingType Sharing { get; init; } = SharingType.Shared;

        public DependencyCreationType CreationType { get; init; } = DependencyCreationType.MustExist;

        public string[] Tags { get; init; } = Array.Empty<string>();

        public RequiresLogicBlockInterfaceAttribute(Type forInterface)
        {
            ForInterface = forInterface;
        }
    }
}
