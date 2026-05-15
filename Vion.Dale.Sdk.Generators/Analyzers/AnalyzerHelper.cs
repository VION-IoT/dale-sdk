using System.Linq;
using Microsoft.CodeAnalysis;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     Shared helpers for attribute detection and type checking in Dale analyzers.
    ///     Attributes are matched by fully-qualified metadata name (standard Roslyn analyzer pattern).
    /// </summary>
    internal static class AnalyzerHelper
    {
        // Attribute full names
        internal const string ServiceProviderContractBindingAttribute = "Vion.Dale.Sdk.Core.ServiceProviderContractBindingAttribute";

        internal const string TimerAttribute = "Vion.Dale.Sdk.Core.TimerAttribute";

        internal const string ServicePropertyAttribute = "Vion.Dale.Sdk.Core.ServicePropertyAttribute";

        internal const string ServiceMeasuringPointAttribute = "Vion.Dale.Sdk.Core.ServiceMeasuringPointAttribute";

        internal const string PersistentAttribute = "Vion.Dale.Sdk.Core.PersistentAttribute";

        internal const string PresentationAttribute = "Vion.Dale.Sdk.Core.PresentationAttribute";

        internal const string LogicBlockContractAttribute = "Vion.Dale.Sdk.Core.LogicBlockContractAttribute";

        internal const string CommandAttribute = "Vion.Dale.Sdk.Core.CommandAttribute";

        internal const string StateUpdateAttribute = "Vion.Dale.Sdk.Core.StateUpdateAttribute";

        internal const string RequestResponseAttribute = "Vion.Dale.Sdk.Core.RequestResponseAttribute";

        internal const string ServiceProviderContractTypeAttribute = "Vion.Dale.Sdk.Configuration.Contract.ServiceProviderContractTypeAttribute";

        /// <summary>
        ///     Checks whether a symbol has an attribute with the given fully-qualified name.
        /// </summary>
        internal static bool HasAttribute(ISymbol symbol, string attributeFullName)
        {
            return symbol.GetAttributes().Any(a => GetFullName(a.AttributeClass) == attributeFullName);
        }

        /// <summary>
        ///     Gets the first attribute data matching the given fully-qualified name, or null.
        /// </summary>
        internal static AttributeData? GetAttribute(ISymbol symbol, string attributeFullName)
        {
            return symbol.GetAttributes().FirstOrDefault(a => GetFullName(a.AttributeClass) == attributeFullName);
        }

        /// <summary>
        ///     Gets the fully-qualified metadata name of a type symbol (without "global::" prefix).
        /// </summary>
        internal static string? GetFullName(INamedTypeSymbol? type)
        {
            if (type == null)
            {
                return null;
            }

            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
        }

        /// <summary>
        ///     Checks whether a type (or any of its interfaces) carries [ServiceProviderContractType].
        ///     Mirrors DeclarativeContractBinder.IsContractType() which checks the type directly.
        /// </summary>
        internal static bool IsServiceProviderContractType(ITypeSymbol type)
        {
            // Check the type itself (matches runtime IsContractType behavior for interface-typed properties)
            if (type is INamedTypeSymbol namedType && HasAttribute(namedType, ServiceProviderContractTypeAttribute))
            {
                return true;
            }

            // Also check AllInterfaces as safety net for concrete-typed properties
            foreach (var iface in type.AllInterfaces)
            {
                if (HasAttribute(iface, ServiceProviderContractTypeAttribute))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Checks whether a type is supported for service properties / measuring points.
        ///     Supports: bool, string, byte, short, ushort, int, uint, long, float, double, DateTime,
        ///     TimeSpan, any enum, any flat readonly record struct (with primitive/enum/string/nullable fields),
        ///     ImmutableArray&lt;T&gt; where T is any of the above, and T? for value types and string.
        ///     decimal is intentionally excluded per the rich-types spec (§5.1).
        /// </summary>
        internal static bool IsSupportedServiceElementType(ITypeSymbol type)
        {
            if (type is null)
            {
                return false;
            }

            return type switch
            {
                // Nullable<T> for value types
                INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt => IsSupportedServiceElementType(nt.TypeArguments[0]),

                // ImmutableArray<T> from System.Collections.Immutable
                INamedTypeSymbol { Name: "ImmutableArray", ContainingNamespace.Name: "Immutable" } ia => IsSupportedServiceElementType(ia.TypeArguments[0]),

                // string / string? — reference type; nullable-ness is at the annotation level
                _ when type.SpecialType == SpecialType.System_String => true,

                // Enums
                _ when type.TypeKind == TypeKind.Enum => true,

                // Primitives: bool, byte, short, ushort, int, uint, long, float, double, DateTime
                _ when type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                           or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_Single or SpecialType.System_Double
                           or SpecialType.System_DateTime => true,

                // TimeSpan (no SpecialType for TimeSpan)
                _ when type.ToDisplayString() == "System.TimeSpan" => true,

                // Flat readonly record struct with primitive/enum/nullable-of-primitive-or-enum fields
                INamedTypeSymbol { IsValueType: true, IsRecord: true, IsReadOnly: true } rs when AllStructFieldsArePrimitiveOrEnum(rs) => true,

                _ => false,
            };
        }

        /// <summary>
        ///     Returns true when <paramref name="namedType" /> is a flat readonly record struct:
        ///     <c>IsValueType == true</c>, <c>IsRecord == true</c>, <c>IsReadOnly == true</c>, and
        ///     every primary-constructor parameter is a primitive, enum, string, TimeSpan, or nullable
        ///     of the same. Nested structs and arrays are not allowed — per spec §5.2 structs are flat.
        /// </summary>
        internal static bool IsFlatReadonlyRecordStruct(INamedTypeSymbol namedType)
        {
            if (!namedType.IsValueType || !namedType.IsRecord || !namedType.IsReadOnly)
            {
                return false;
            }

            return AllStructFieldsArePrimitiveOrEnum(namedType);
        }

        /// <summary>
        ///     Returns true when every parameter of the primary positional constructor of
        ///     <paramref name="structType" /> is a primitive, enum, string, TimeSpan, or nullable
        ///     of the same. Nested structs and arrays are not allowed — per spec §5.2 structs are flat.
        /// </summary>
        internal static bool AllStructFieldsArePrimitiveOrEnum(INamedTypeSymbol structType)
        {
            // Inspect the positional record-struct primary constructor's parameters.
            var ctor = structType.InstanceConstructors.FirstOrDefault(c => c.Parameters.Length > 0);
            if (ctor is null)
            {
                return false;
            }

            foreach (var p in ctor.Parameters)
            {
                var t = p.Type;
                var isNullable = t is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
                var inner = isNullable ? ((INamedTypeSymbol)t).TypeArguments[0] : t;

                var ok = inner.SpecialType is SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                             or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_Single or SpecialType.System_Double
                             or SpecialType.System_DateTime || inner.TypeKind == TypeKind.Enum || inner.SpecialType == SpecialType.System_String ||
                         inner.ToDisplayString() == "System.TimeSpan";

                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Gets a named argument value from an AttributeData, or default if not present.
        /// </summary>
        internal static T? GetNamedArgument<T>(AttributeData attribute, string name)
        {
            foreach (var kvp in attribute.NamedArguments)
            {
                if (kvp.Key == name && kvp.Value.Value is T value)
                {
                    return value;
                }
            }

            return default;
        }
    }
}
