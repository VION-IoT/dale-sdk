using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     Shared detection + parse helpers for the RFC 0004 emission-policy analyzers (DALE034–DALE039).
    ///     Mirrors the runtime's <c>DurationParser</c> grammar and the built-in
    ///     <c>IChangeThreshold&lt;T&gt;</c> registrations so a compile-time diagnostic agrees with what the
    ///     runtime would accept.
    /// </summary>
    internal static class EmissionAttributeHelper
    {
        internal const string IChangeThresholdMetadataName = "Vion.Dale.Sdk.Emission.IChangeThreshold`1";

        /// <summary>
        ///     The value types the runtime ships a built-in <c>IChangeThreshold&lt;T&gt;</c> for: double,
        ///     float, decimal, int, long, and <c>System.TimeSpan</c>. Kept in lock-step with
        ///     <c>ChangeThresholdRegistry</c>'s static constructor.
        /// </summary>
        internal static bool IsBuiltInThresholdType(ITypeSymbol type)
        {
            return type.SpecialType is SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal or SpecialType.System_Int32
                       or SpecialType.System_Int64 || type.ToDisplayString() == "System.TimeSpan";
        }

        /// <summary>
        ///     Returns the <c>[ServiceProperty]</c> or <c>[ServiceMeasuringPoint]</c> attribute carrying the
        ///     emission knobs, or <c>null</c> when the property has neither.
        /// </summary>
        internal static AttributeData? GetEmissionAttribute(IPropertySymbol property)
        {
            return AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServicePropertyAttribute) ??
                   AnalyzerHelper.GetAttribute(property, AnalyzerHelper.ServiceMeasuringPointAttribute);
        }

        /// <summary>
        ///     Reads the <c>MinChange</c> string literal from the attribute, or <c>null</c> when it is unset
        ///     or set to <c>null</c> / empty.
        /// </summary>
        internal static string? GetMinChange(AttributeData attribute)
        {
            foreach (var kvp in attribute.NamedArguments)
            {
                if (kvp.Key == "MinChange" && kvp.Value.Value is string s && !string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }

            return null;
        }

        /// <summary>
        ///     Reads the explicitly-set <c>MinInterval</c> string literal, or <c>null</c> when the author did
        ///     not write it (in which case the attribute default of <c>"250ms"</c> applies — indistinguishable
        ///     from an explicit <c>"250ms"</c>, so callers that need the effective value should default it).
        /// </summary>
        internal static string? GetExplicitMinInterval(AttributeData attribute)
        {
            foreach (var kvp in attribute.NamedArguments)
            {
                if (kvp.Key == "MinInterval" && kvp.Value.Value is string s)
                {
                    return s;
                }
            }

            return null;
        }

        /// <summary>
        ///     Reads the <c>Immediate</c> flag (default <c>false</c>).
        /// </summary>
        internal static bool GetImmediate(AttributeData attribute)
        {
            foreach (var kvp in attribute.NamedArguments)
            {
                if (kvp.Key == "Immediate" && kvp.Value.Value is bool b)
                {
                    return b;
                }
            }

            return false;
        }

        /// <summary>
        ///     Unwraps <c>Nullable&lt;T&gt;</c> to its underlying type; returns the type unchanged otherwise.
        /// </summary>
        internal static ITypeSymbol Unwrap(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nt)
            {
                return nt.TypeArguments[0];
            }

            return type;
        }

        /// <summary>
        ///     For a built-in numeric type or <c>TimeSpan</c>, checks whether <paramref name="minChange" />
        ///     parses with that type's known format and, when it does not, yields the expected-format hint.
        ///     Returns <c>false</c> (with a null hint) for types whose <c>MinChange</c> format is opaque
        ///     (custom-threshold types), signalling "do not parse-check".
        /// </summary>
        internal static bool TryGetParseExpectation(ITypeSymbol valueType, string minChange, out bool parses, out string? expectationHint)
        {
            parses = true;
            expectationHint = null;

            switch (valueType.SpecialType)
            {
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                    expectationHint = "An invariant-culture integer";
                    parses = ParsesAsInteger(minChange);
                    return true;
                case SpecialType.System_Double:
                case SpecialType.System_Single:
                    expectationHint = "An invariant-culture number";
                    parses = ParsesAsFloat(minChange);
                    return true;
                case SpecialType.System_Decimal:
                    // Runtime decimal threshold uses NumberStyles.Number (thousands separators allowed,
                    // no exponent) — mirror it exactly so a valid runtime token is never flagged.
                    expectationHint = "An invariant-culture number";
                    parses = ParsesAsDecimal(minChange);
                    return true;
            }

            if (valueType.ToDisplayString() == "System.TimeSpan")
            {
                expectationHint = "A duration (number with optional us/ms/s/m/h suffix)";
                parses = TryParseDuration(minChange, out _);
                return true;
            }

            // Opaque format (custom threshold) — not parse-checkable.
            return false;
        }

        /// <summary>
        ///     <c>true</c> when the duration string is the throttle-disabling sentinel <c>"0"</c> /
        ///     <c>"0ms"</c> (case-insensitive on the suffix, whitespace tolerated).
        /// </summary>
        internal static bool IsDisablingSentinel(string token)
        {
            if (!TryParseDuration(token, out var ticks))
            {
                return false;
            }

            return ticks == 0;
        }

        /// <summary>
        ///     Parses the runtime duration grammar: a number with an optional <c>us</c>/<c>ms</c>/<c>s</c>/
        ///     <c>m</c>/<c>h</c> suffix; a bare number is milliseconds. Returns the value in ticks
        ///     (100&#160;ns). Mirrors <c>Vion.Dale.Sdk.Emission.DurationParser</c>.
        /// </summary>
        internal static bool TryParseDuration(string token, out long ticks)
        {
            ticks = 0;
            if (token == null)
            {
                return false;
            }

            var trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            var splitIndex = trimmed.Length;
            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                var isNumeric = (c >= '0' && c <= '9') || c == '.' || c == '+' || c == '-';
                if (!isNumeric)
                {
                    splitIndex = i;
                    break;
                }
            }

            var numberPart = trimmed.Substring(0, splitIndex);
            var unitPart = trimmed.Substring(splitIndex).Trim().ToLowerInvariant();

            if (numberPart.Length == 0)
            {
                return false;
            }

            if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            const double ticksPerMillisecond = 10_000.0;
            switch (unitPart)
            {
                case "":
                case "ms":
                    ticks = (long)(value * ticksPerMillisecond);
                    return true;
                case "us":
                    ticks = (long)(value * 10.0);
                    return true;
                case "s":
                    ticks = (long)(value * 1_000 * ticksPerMillisecond);
                    return true;
                case "m":
                    ticks = (long)(value * 60 * 1_000 * ticksPerMillisecond);
                    return true;
                case "h":
                    ticks = (long)(value * 60 * 60 * 1_000 * ticksPerMillisecond);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        ///     Collects every value type <c>T</c> for which a non-interface, instantiable
        ///     <c>IChangeThreshold&lt;T&gt;</c> implementation is visible in the compilation closure — the
        ///     compilation's own assembly plus the referenced assemblies that reference the SDK (a shared
        ///     foundation library that declares the threshold). This is the compile-time mirror of the runtime
        ///     <c>ChangeThresholdRegistry</c> assembly scan, so a passing compile implies a working runtime
        ///     deadband even when the threshold lives in a referenced library. Built once per compilation.
        /// </summary>
        internal static ImmutableHashSet<ITypeSymbol> CollectCustomChangeThresholdTypes(Compilation compilation, INamedTypeSymbol ichangeThreshold)
        {
            var sdkAssembly = ichangeThreshold.ContainingAssembly;
            var builder = ImmutableHashSet.CreateBuilder<ITypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var assembly in RelevantAssemblies(compilation, sdkAssembly))
            {
                foreach (var type in EnumerateNamedTypes(assembly.GlobalNamespace))
                {
                    if (type.TypeKind == TypeKind.Interface || type.IsAbstract)
                    {
                        continue;
                    }

                    foreach (var iface in type.AllInterfaces)
                    {
                        if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, ichangeThreshold) && iface.TypeArguments.Length == 1)
                        {
                            builder.Add(iface.TypeArguments[0]);
                        }
                    }
                }
            }

            return builder.ToImmutable();
        }

        /// <summary>
        ///     The compilation's own assembly plus referenced assemblies that reference the SDK — only those
        ///     can declare an <c>IChangeThreshold&lt;T&gt;</c>. Skipping the rest (framework, unrelated NuGet)
        ///     keeps the one-time scan cheap; skipping the SDK itself avoids re-scanning the built-ins.
        /// </summary>
        private static IEnumerable<IAssemblySymbol> RelevantAssemblies(Compilation compilation, IAssemblySymbol sdkAssembly)
        {
            yield return compilation.Assembly;

            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol referenced && !SymbolEqualityComparer.Default.Equals(referenced, sdkAssembly) &&
                    ReferencesAssembly(referenced, sdkAssembly))
                {
                    yield return referenced;
                }
            }
        }

        private static bool ReferencesAssembly(IAssemblySymbol assembly, IAssemblySymbol target)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var referenced in module.ReferencedAssemblySymbols)
                {
                    if (SymbolEqualityComparer.Default.Equals(referenced, target))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool ParsesAsInteger(string token)
        {
            return long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        }

        private static bool ParsesAsFloat(string token)
        {
            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }

        private static bool ParsesAsDecimal(string token)
        {
            return decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out _);
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(INamespaceSymbol ns)
        {
            foreach (var member in ns.GetMembers())
            {
                if (member is INamespaceSymbol childNs)
                {
                    foreach (var nested in EnumerateNamedTypes(childNs))
                    {
                        yield return nested;
                    }
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;

                    foreach (var nestedType in EnumerateNestedTypes(type))
                    {
                        yield return nestedType;
                    }
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
        {
            foreach (var member in type.GetTypeMembers())
            {
                yield return member;

                foreach (var nested in EnumerateNestedTypes(member))
                {
                    yield return nested;
                }
            }
        }
    }
}