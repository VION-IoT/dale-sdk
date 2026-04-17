using System.Collections.Generic;
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
        internal const string ServiceProviderContractAttribute = "Vion.Dale.Sdk.Core.ServiceProviderContractAttribute";

        internal const string TimerAttribute = "Vion.Dale.Sdk.Core.TimerAttribute";

        internal const string ServicePropertyAttribute = "Vion.Dale.Sdk.Core.ServicePropertyAttribute";

        internal const string ServiceMeasuringPointAttribute = "Vion.Dale.Sdk.Core.ServiceMeasuringPointAttribute";

        internal const string PersistentAttribute = "Vion.Dale.Sdk.Core.PersistentAttribute";

        internal const string StatusIndicatorAttribute = "Vion.Dale.Sdk.Core.StatusIndicatorAttribute";

        internal const string ContractAttribute = "Vion.Dale.Sdk.Core.ContractAttribute";

        internal const string CommandAttribute = "Vion.Dale.Sdk.Core.CommandAttribute";

        internal const string StateUpdateAttribute = "Vion.Dale.Sdk.Core.StateUpdateAttribute";

        internal const string RequestResponseAttribute = "Vion.Dale.Sdk.Core.RequestResponseAttribute";

        internal const string ServiceProviderContractTypeAttribute = "Vion.Dale.Sdk.Configuration.Contract.ServiceProviderContractTypeAttribute";

        /// <summary>
        ///     SpecialType values for built-in supported types.
        ///     Using SpecialType is more reliable than string matching in Roslyn
        ///     because keyword aliases (bool, int, etc.) resolve to SpecialType directly.
        /// </summary>
        private static readonly HashSet<SpecialType> SupportedSpecialTypes = new()
                                                                             {
                                                                                 SpecialType.System_Boolean,
                                                                                 SpecialType.System_String,
                                                                                 SpecialType.System_Int32,
                                                                                 SpecialType.System_Int64,
                                                                                 SpecialType.System_Int16,
                                                                                 SpecialType.System_Single,
                                                                                 SpecialType.System_Double,
                                                                                 SpecialType.System_Decimal,
                                                                                 SpecialType.System_DateTime,
                                                                             };

        /// <summary>
        ///     Full names for non-special supported types (TimeSpan has no SpecialType).
        /// </summary>
        private static readonly HashSet<string> SupportedNonSpecialTypes = new()
                                                                           {
                                                                               "System.TimeSpan",
                                                                           };

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
        ///     Enums are always supported (mapped to Integer at introspection time).
        /// </summary>
        internal static bool IsSupportedServiceElementType(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            if (SupportedSpecialTypes.Contains(type.SpecialType))
            {
                return true;
            }

            var fullName = type is INamedTypeSymbol named ? GetFullName(named) : null;
            return fullName != null && SupportedNonSpecialTypes.Contains(fullName);
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