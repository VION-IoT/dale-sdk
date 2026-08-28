using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     DALE046 — the types declared on <c>[ScenarioWire]</c> must be codec-representable value structs.
    ///     The scenario codec builds the wire value from a JSON scenario value by reflecting over the struct's
    ///     positional constructor, so every member has to be a serializable leaf or a nested readonly record
    ///     struct of the same. A delegate-typed member — a pending-operation callback — or any other reference
    ///     type makes the contract undrivable and unassertable, silently.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ScenarioWireTypeAnalyzer : DiagnosticAnalyzer
    {
        private const string ScenarioWireAttribute = "Vion.Dale.Sdk.Abstractions.ScenarioWireAttribute";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get => ImmutableArray.Create(DaleDiagnostics.DALE046_ScenarioWireTypeNotRepresentable);
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(AnalyzeHandler, SymbolKind.NamedType);
        }

        private static void AnalyzeHandler(SymbolAnalysisContext context)
        {
            var handler = (INamedTypeSymbol)context.Symbol;

            var attribute = AnalyzerHelper.GetAttribute(handler, ScenarioWireAttribute);
            if (attribute is null)
            {
                return;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key != "Inbound" && argument.Key != "Outbound")
                {
                    continue;
                }

                if (argument.Value.Value is not INamedTypeSymbol wireType)
                {
                    continue;
                }

                // An unresolved type argument is a compile error of its own; judging it would only add noise.
                if (wireType.TypeKind == TypeKind.Error)
                {
                    continue;
                }

                var problem = Describe(argument.Key, wireType);
                if (problem is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(DaleDiagnostics.DALE046_ScenarioWireTypeNotRepresentable,
                                                               attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? handler.Locations.FirstOrDefault(),
                                                               problem));
                }
            }
        }

        /// <summary>
        ///     Returns the message describing why the wire type cannot round-trip through the scenario codec,
        ///     or <c>null</c> when it can.
        /// </summary>
        private static string? Describe(string direction, INamedTypeSymbol wireType)
        {
            if (!wireType.IsValueType)
            {
                return $"[ScenarioWire] {direction} type '{wireType.Name}' is not a value struct. Declare the wire type as a readonly record struct.";
            }

            var path = new List<string>();
            var offender = FindUnrepresentableMember(wireType, path, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
            if (offender is null)
            {
                return null;
            }

            var memberPath = string.Join(".", path);
            var kind = offender.TypeKind == TypeKind.Delegate ? "a delegate" : $"of type '{offender.ToDisplayString()}'";

            return $"[ScenarioWire] {direction} type '{wireType.Name}' has member '{memberPath}' {kind}, which a scenario value cannot be built from. " +
                   "Wire members must be primitives, string, enums, DateTime/DateTimeOffset/TimeSpan/Guid, nullables of those, or nested readonly record structs of the same.";
        }

        /// <summary>
        ///     Walks the wire struct the way the codec does — the longest positional constructor, descending
        ///     nested readonly record structs — and returns the first member type that cannot be represented,
        ///     recording its dotted path.
        /// </summary>
        private static ITypeSymbol? FindUnrepresentableMember(INamedTypeSymbol structType, List<string> path, HashSet<INamedTypeSymbol> visited)
        {
            // A value type cannot contain itself, but a broken compilation can still present a cycle.
            if (!visited.Add(structType))
            {
                return null;
            }

            var constructor = structType.InstanceConstructors
                                        .Where(c => c.Parameters.Length > 0 && !IsCopyConstructor(c, structType))
                                        .OrderByDescending(c => c.Parameters.Length)
                                        .FirstOrDefault();

            if (constructor is null)
            {
                return null;
            }

            foreach (var parameter in constructor.Parameters)
            {
                path.Add(parameter.Name);

                var type = Unwrap(parameter.Type);
                if (IsRepresentableLeaf(type))
                {
                    path.RemoveAt(path.Count - 1);
                    continue;
                }

                if (type is INamedTypeSymbol nested && IsReadonlyRecordStruct(nested))
                {
                    var offender = FindUnrepresentableMember(nested, path, visited);
                    if (offender is not null)
                    {
                        return offender;
                    }

                    path.RemoveAt(path.Count - 1);
                    continue;
                }

                return type;
            }

            return null;
        }

        /// <summary>Peels a nullable value type; every other type is returned unchanged.</summary>
        private static ITypeSymbol Unwrap(ITypeSymbol type)
        {
            return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;
        }

        private static bool IsRepresentableLeaf(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Char:
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_String:
                case SpecialType.System_DateTime:
                    return true;
            }

            var name = type.ToDisplayString();

            return name == "System.DateTimeOffset" || name == "System.TimeSpan" || name == "System.Guid";
        }

        private static bool IsReadonlyRecordStruct(INamedTypeSymbol type)
        {
            return type is { IsValueType: true, IsReadOnly: true } && (type.IsRecord || HasRecordStructMarker(type));
        }

        /// <summary>
        ///     A record struct loaded from metadata does not report IsRecord; the synthesized Deconstruct a
        ///     positional record always emits stands in for it.
        /// </summary>
        private static bool HasRecordStructMarker(INamedTypeSymbol type)
        {
            return type.GetMembers("Deconstruct").OfType<IMethodSymbol>().Any();
        }

        private static bool IsCopyConstructor(IMethodSymbol constructor, INamedTypeSymbol structType)
        {
            return constructor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, structType);
        }
    }
}