using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Vion.Dale.Sdk.Generators.Predicates;

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

        internal const string StructFieldAttribute = "Vion.Dale.Sdk.Core.StructFieldAttribute";

        internal const string PersistentAttribute = "Vion.Dale.Sdk.Core.PersistentAttribute";

        internal const string PresentationAttribute = "Vion.Dale.Sdk.Core.PresentationAttribute";

        internal const string ServiceInterfaceAttribute = "Vion.Dale.Sdk.Core.ServiceInterfaceAttribute";

        internal const string LogicBlockBaseType = "Vion.Dale.Sdk.Core.LogicBlockBase";

        internal const string LogicBlockContractAttribute = "Vion.Dale.Sdk.Core.LogicBlockContractAttribute";

        internal const string CommandAttribute = "Vion.Dale.Sdk.Core.CommandAttribute";

        internal const string StateUpdateAttribute = "Vion.Dale.Sdk.Core.StateUpdateAttribute";

        internal const string RequestResponseAttribute = "Vion.Dale.Sdk.Core.RequestResponseAttribute";

        internal const string ServiceProviderContractTypeAttribute = "Vion.Dale.Sdk.Configuration.Contract.ServiceProviderContractTypeAttribute";

        internal const string LogicBlockInterfaceBindingAttribute = "Vion.Dale.Sdk.Core.LogicBlockInterfaceBindingAttribute";

        internal const string LogicInterfaceAttribute = "Vion.Dale.Sdk.CodeGeneration.LogicInterfaceAttribute";

        // RFC 0016 config-time structural gating
        internal const string IncludedWhenAttribute = "Vion.Dale.Sdk.Core.IncludedWhenAttribute";

        internal const string InstantiationParameterAttribute = "Vion.Dale.Sdk.Core.InstantiationParameterAttribute";

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

                // TimeSpan / Guid (no SpecialType)
                _ when type.ToDisplayString() == "System.TimeSpan" => true,
                _ when type.ToDisplayString() == "System.Guid" => true,

                // Flat readonly record struct with primitive/enum/nullable-of-primitive-or-enum fields
                INamedTypeSymbol named when IsFlatReadonlyRecordStruct(named) => true,

                _ => false,
            };
        }

        /// <summary>
        ///     True when <paramref name="type" /> renders as a single scalar value on a dashboard tile:
        ///     a numeric / bool primitive, string, enum, DateTime, or TimeSpan. Composite supported types
        ///     (flat record struct, ImmutableArray&lt;T&gt;) are not scalar. Unwrap Nullable&lt;T&gt; first.
        /// </summary>
        internal static bool IsScalarTileType(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            if (type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
                or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_DateTime
                or SpecialType.System_String)
            {
                return true;
            }

            return type.ToDisplayString() == "System.TimeSpan" || type.ToDisplayString() == "System.Guid";
        }

        /// <summary>
        ///     Returns true when <paramref name="namedType" /> is a flat readonly record struct:
        ///     <c>IsValueType == true</c>, <c>IsReadOnly == true</c>, record-struct-shaped, and every
        ///     primary-constructor parameter is a primitive, enum, string, TimeSpan, or nullable of
        ///     the same. Nested structs and arrays are not allowed — per spec §5.2 structs are flat.
        ///     <para>
        ///         <see cref="INamedTypeSymbol.IsRecord" /> is known to return <c>false</c> for record
        ///         structs loaded from metadata (Roslyn detects records via the <c>&lt;Clone&gt;$</c>
        ///         synthesized method, which record structs have never had — see dotnet/roslyn#63566).
        ///         So a bit-identical struct produces <c>IsRecord == true</c> from source and
        ///         <c>IsRecord == false</c> from a referenced assembly, even with the same compiler.
        ///         For metadata-loaded types we fall back to the synthesized <c>Deconstruct</c>
        ///         method, which positional records (class and struct) always emit and which plain
        ///         structs — including C# 12+ structs with primary constructors and system types like
        ///         <c>decimal</c> — never receive.
        ///     </para>
        /// </summary>
        internal static bool IsFlatReadonlyRecordStruct(INamedTypeSymbol namedType)
        {
            if (!namedType.IsValueType || !namedType.IsReadOnly)
            {
                return false;
            }

            if (!namedType.IsRecord && !HasRecordStructMarker(namedType))
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
                         inner.ToDisplayString() == "System.TimeSpan" || inner.ToDisplayString() == "System.Guid";

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

        /// <summary>
        ///     Public, non-static instance properties across the self + base chain (stops before
        ///     <see cref="object" />) — the exact set the declarative binders bind
        ///     (<c>BindingFlags.Public | Instance</c>). The most-derived declaration of a name appears first.
        /// </summary>
        internal static IEnumerable<IPropertySymbol> EnumerateProperties(INamedTypeSymbol type)
        {
            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (property.DeclaredAccessibility == Accessibility.Public && !property.IsStatic)
                    {
                        yield return property;
                    }
                }
            }
        }

        internal static bool InheritsFromLogicBlockBase(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (GetFullName(current) == LogicBlockBaseType)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsServiceElement(IPropertySymbol property)
        {
            return HasAttribute(property, ServicePropertyAttribute) || HasAttribute(property, ServiceMeasuringPointAttribute);
        }

        /// <summary>
        ///     Whether <paramref name="type" /> carries service members (own/inherited service props or measuring points, or
        ///     a <c>[ServiceInterface]</c>).
        /// </summary>
        internal static bool TypeHasServiceMembers(INamedTypeSymbol type)
        {
            foreach (var property in EnumerateProperties(type))
            {
                if (IsServiceElement(property))
                {
                    return true;
                }
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (!HasAttribute(iface, ServiceInterfaceAttribute))
                {
                    continue;
                }

                foreach (var property in iface.GetMembers().OfType<IPropertySymbol>())
                {
                    if (IsServiceElement(property))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Maps a referenced property's CLR type to a predicate <see cref="RefCategory" /> (Nullable unwrapped).</summary>
        internal static RefCategory Categorize(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                type = nullable.TypeArguments[0];
            }

            if (type.TypeKind == TypeKind.Enum)
            {
                return RefCategory.Enum;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    return RefCategory.Bool;
                case SpecialType.System_String:
                    return RefCategory.String;
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return RefCategory.Double;
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    return RefCategory.Integer;
                default:
                    return RefCategory.Other;
            }
        }

        /// <summary>The case-sensitive enum member names (const fields) of an enum type, or <c>null</c> for a non-enum.</summary>
        internal static IReadOnlyCollection<string>? CollectEnumMembers(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                type = nullable.TypeArguments[0];
            }

            if (type.TypeKind != TypeKind.Enum)
            {
                return null;
            }

            var names = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.IsConst)
                {
                    names.Add(member.Name);
                }
            }

            return names;
        }

        /// <summary>
        ///     Builds a <see cref="PredicateMember" /> for a referenced property from its type + optional
        ///     <c>[ServiceProperty]</c>.
        /// </summary>
        internal static PredicateMember MakeMember(ITypeSymbol type, AttributeData? serviceProperty)
        {
            var isServiceProperty = serviceProperty is not null;
            var isWriteOnly = isServiceProperty && GetNamedArgument<bool>(serviceProperty!, "WriteOnly");
            var category = Categorize(type);
            var enumMembers = category == RefCategory.Enum ? CollectEnumMembers(type) : null;
            return new PredicateMember(category, isServiceProperty, isWriteOnly, enumMembers);
        }

        /// <summary>
        ///     True if <paramref name="type" /> carries a synthesized <c>Deconstruct</c> method
        ///     — auto-emitted for every positional record (class and struct) and never for plain
        ///     structs (even C# 12+ structs with primary constructors) or system types like
        ///     <c>decimal</c>. Used as a fallback when <see cref="INamedTypeSymbol.IsRecord" />
        ///     reports <c>false</c> for record structs loaded from metadata.
        /// </summary>
        private static bool HasRecordStructMarker(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers("Deconstruct"))
            {
                if (member is IMethodSymbol)
                {
                    return true;
                }
            }

            return false;
        }
    }
}