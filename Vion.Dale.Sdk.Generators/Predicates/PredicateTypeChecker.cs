using System.Collections.Generic;
using System.Linq;

namespace Vion.Dale.Sdk.Generators.Predicates
{
    /// <summary>Type category of a referenced service property, for predicate type-checking.</summary>
    internal enum RefCategory
    {
        Bool,

        Enum,

        Integer,

        String,

        /// <summary><c>double</c> / <c>float</c> — excluded (analog values flap): DALE042.</summary>
        Double,

        /// <summary>Anything else (struct, array, DateTime, TimeSpan, Guid): DALE042.</summary>
        Other,
    }

    /// <summary>A referenceable service member in the block's service map.</summary>
    internal sealed class PredicateMember
    {
        public RefCategory Category { get; }

        /// <summary>False for measuring-point-only members (no <c>[ServiceProperty]</c>) — those are not referenceable.</summary>
        public bool IsServiceProperty { get; }

        public bool IsWriteOnly { get; }

        /// <summary>
        ///     The enum's member names (case-sensitive), when <see cref="Category" /> is
        ///     <see cref="RefCategory.Enum" /> — so a quoted literal can be checked against the real
        ///     members. <c>null</c> for non-enum members, or when the members could not be resolved
        ///     (in which case membership is not validated, to avoid false positives).
        /// </summary>
        public IReadOnlyCollection<string>? EnumMembers { get; }

        public PredicateMember(RefCategory category, bool isServiceProperty, bool isWriteOnly, IReadOnlyCollection<string>? enumMembers = null)
        {
            Category = category;
            IsServiceProperty = isServiceProperty;
            IsWriteOnly = isWriteOnly;
            EnumMembers = enumMembers;
        }
    }

    /// <summary>One service (root or component) and its referenceable members, keyed by property name (ordinal).</summary>
    internal sealed class PredicateService
    {
        public IReadOnlyDictionary<string, PredicateMember> Members { get; }

        public PredicateService(IReadOnlyDictionary<string, PredicateMember> members)
        {
            Members = members;
        }
    }

    /// <summary>
    ///     The resolution context for one annotated member: the block's full service map plus the
    ///     identifier of the annotated member's own service (against which bare refs resolve).
    /// </summary>
    internal sealed class PredicateContext
    {
        /// <summary>Keyed by service identifier (root class name + component holding-property names), ordinal.</summary>
        public IReadOnlyDictionary<string, PredicateService> Services { get; }

        public string OwnServiceId { get; }

        public PredicateContext(IReadOnlyDictionary<string, PredicateService> services, string ownServiceId)
        {
            Services = services;
            OwnServiceId = ownServiceId;
        }
    }

    /// <summary>A type-check finding; <see cref="IsTypeError" /> routes to DALE042 (true) or DALE041 (false).</summary>
    internal sealed class PredicateCheckError
    {
        public string Message { get; }

        public bool IsTypeError { get; }

        public PredicateCheckError(string message, bool isTypeError)
        {
            Message = message;
            IsTypeError = isTypeError;
        }

        public static PredicateCheckError Resolve(string message)
        {
            return new PredicateCheckError(message, false);
        }

        public static PredicateCheckError Type(string message)
        {
            return new PredicateCheckError(message, true);
        }
    }

    /// <summary>
    ///     Walks a parsed predicate AST against a <see cref="PredicateContext" /> and applies the
    ///     reference-resolution (DALE041) and type-discipline (DALE042) rules of
    ///     <c>docs/predicates.md</c> §2.3 / §3. Never evaluates.
    /// </summary>
    internal static class PredicateTypeChecker
    {
        public static IReadOnlyList<PredicateCheckError> Check(PredicateNode node, PredicateContext context)
        {
            var errors = new List<PredicateCheckError>();
            Visit(node, context, errors);
            return errors;
        }

        private static void Visit(PredicateNode node, PredicateContext ctx, List<PredicateCheckError> errors)
        {
            switch (node)
            {
                case OrNode or:
                    Visit(or.Left, ctx, errors);
                    Visit(or.Right, ctx, errors);
                    break;
                case AndNode and:
                    Visit(and.Left, ctx, errors);
                    Visit(and.Right, ctx, errors);
                    break;
                case NotNode not:
                    Visit(not.Operand, ctx, errors);
                    break;
                case BoolRefNode boolRef:
                    CheckBoolRef(boolRef, ctx, errors);
                    break;
                case ComparisonNode comparison:
                    CheckComparison(comparison, ctx, errors);
                    break;
                case MembershipNode membership:
                    CheckMembership(membership, ctx, errors);
                    break;
            }
        }

        private static void CheckBoolRef(BoolRefNode node, PredicateContext ctx, List<PredicateCheckError> errors)
        {
            var member = Resolve(node.Reference, ctx, errors);
            if (member is null)
            {
                return;
            }

            if (member.Category != RefCategory.Bool)
            {
                errors.Add(PredicateCheckError.Type($"a bare reference '{node.Reference.Text}' must be a bool service property (it is {Describe(member.Category)})"));
            }
        }

        private static void CheckComparison(ComparisonNode node, PredicateContext ctx, List<PredicateCheckError> errors)
        {
            var member = Resolve(node.Reference, ctx, errors);
            if (member is null)
            {
                return;
            }

            if (node.IsRelational)
            {
                if (member.Category != RefCategory.Integer)
                {
                    errors.Add(PredicateCheckError
                                   .Type($"relational operator '{node.Operator}' requires an integer reference; '{node.Reference.Text}' is {Describe(member.Category)}"));
                    return;
                }

                if (node.Literal.Kind != PredicateLiteralKind.Integer)
                {
                    errors.Add(PredicateCheckError.Type($"relational operator '{node.Operator}' requires an integer literal on the right of '{node.Reference.Text}'"));
                }

                return;
            }

            // Equality: literal must match the reference type (and, for enums, be a real member name).
            if (!LiteralMatches(member, node.Literal, out var expected))
            {
                errors.Add(PredicateCheckError.Type($"'{node.Reference.Text}' is {Describe(member.Category)}; the '{node.Operator}' literal must be {expected}"));
            }
        }

        private static void CheckMembership(MembershipNode node, PredicateContext ctx, List<PredicateCheckError> errors)
        {
            var member = Resolve(node.Reference, ctx, errors);
            if (member is null)
            {
                return;
            }

            if (member.Category is not (RefCategory.Enum or RefCategory.Integer or RefCategory.String))
            {
                errors.Add(PredicateCheckError.Type($"'in' requires an enum, string, or integer reference; '{node.Reference.Text}' is {Describe(member.Category)}"));
                return;
            }

            foreach (var literal in node.Items)
            {
                if (!LiteralMatches(member, literal, out var expected))
                {
                    errors.Add(PredicateCheckError.Type($"every element of the 'in' list for '{node.Reference.Text}' must be {expected}"));
                    return;
                }
            }
        }

        /// <summary>
        ///     Resolves a reference to a referenceable member, emitting DALE041 (unresolved / non-service-property /
        ///     ambiguous) or DALE042 (WriteOnly / unsupported type) errors. Returns the member when it resolves to a
        ///     usable service property, otherwise <c>null</c>.
        /// </summary>
        private static PredicateMember? Resolve(PredicateRef reference, PredicateContext ctx, List<PredicateCheckError> errors)
        {
            if (reference.IsQualified)
            {
                var serviceId = reference.Service!;

                // Name-collision rule: the first segment resolves service-first, but if it also names a
                // property on the annotated member's own service the intent is ambiguous — DALE041.
                if (ctx.Services.TryGetValue(ctx.OwnServiceId, out var ownService) && ownService.Members.ContainsKey(serviceId))
                {
                    errors.Add(PredicateCheckError
                                   .Resolve($"reference '{reference.Text}' is ambiguous: '{serviceId}' is both a sibling-service identifier and a property on service '{ctx.OwnServiceId}'"));
                    return null;
                }

                if (!ctx.Services.TryGetValue(serviceId, out var service))
                {
                    errors.Add(PredicateCheckError
                                   .Resolve($"'{serviceId}' is not a sibling-service identifier (expected the block class name or a component-service property name)"));
                    return null;
                }

                if (!service.Members.TryGetValue(reference.Property, out var qualifiedMember))
                {
                    errors.Add(PredicateCheckError.Resolve($"service '{serviceId}' has no service property '{reference.Property}'"));
                    return null;
                }

                return Validate(reference, qualifiedMember, errors);
            }

            // Bare ref: resolves ONLY against the annotated member's own service.
            if (!ctx.Services.TryGetValue(ctx.OwnServiceId, out var own))
            {
                errors.Add(PredicateCheckError.Resolve($"bare reference '{reference.Property}' cannot be resolved (own service '{ctx.OwnServiceId}' not found)"));
                return null;
            }

            // A bare ref that names ANY sibling-service identifier is ambiguous — DALE041 — even when the
            // own service also has a property of that name. The dashboard evaluation context is a flat
            // namespace, so the sibling-service OBJECT shadows the same-named property (mirror of the
            // qualified-ref rule above; spec §3).
            if (ctx.Services.ContainsKey(reference.Property))
            {
                errors.Add(PredicateCheckError
                               .Resolve($"bare reference '{reference.Property}' collides with a sibling-service identifier; in the flat evaluation context the service would shadow the property — rename one, or qualify a sibling property as '{reference.Property}.<Property>'"));
                return null;
            }

            if (!own.Members.TryGetValue(reference.Property, out var member))
            {
                errors.Add(PredicateCheckError
                               .Resolve($"service '{ctx.OwnServiceId}' has no service property '{reference.Property}' (a bare reference must be a property on the same service)"));
                return null;
            }

            return Validate(reference, member, errors);
        }

        private static PredicateMember? Validate(PredicateRef reference, PredicateMember member, List<PredicateCheckError> errors)
        {
            if (!member.IsServiceProperty)
            {
                errors.Add(PredicateCheckError.Resolve($"'{reference.Text}' is a measuring-point-only member; predicates may reference [ServiceProperty] members only"));
                return null;
            }

            if (member.IsWriteOnly)
            {
                errors.Add(PredicateCheckError.Type($"'{reference.Text}' is a WriteOnly property; its value is redacted in the UI and cannot be referenced"));
                return null;
            }

            if (member.Category is RefCategory.Double or RefCategory.Other)
            {
                errors.Add(PredicateCheckError.Type($"'{reference.Text}' is {Describe(member.Category)}; predicates may reference bool, enum, integer, or string properties only"));
                return null;
            }

            return member;
        }

        private static bool LiteralMatches(PredicateMember member, PredicateLiteral literal, out string expected)
        {
            switch (member.Category)
            {
                case RefCategory.Bool:
                    expected = "true or false";
                    return literal.Kind == PredicateLiteralKind.Boolean;
                case RefCategory.Integer:
                    expected = "an integer";
                    return literal.Kind == PredicateLiteralKind.Integer;
                case RefCategory.Enum:
                    if (literal.Kind != PredicateLiteralKind.String)
                    {
                        expected = "a quoted enum member (e.g. 'Eco')";
                        return false;
                    }

                    // Validate against the enum's ACTUAL member names — a typo (Mode == 'Ecoo') must fail
                    // CLOSED (DALE042), not build clean and permanently hide the row in every UI. When the
                    // members could not be resolved, skip the check (don't false-positive).
                    if (member.EnumMembers is not null && !member.EnumMembers.Contains(literal.StringValue))
                    {
                        expected = member.EnumMembers.Count == 0 ? "a quoted enum member" : "one of the enum members: " + string.Join(", ", member.EnumMembers);
                        return false;
                    }

                    expected = "a quoted enum member (e.g. 'Eco')";
                    return true;
                case RefCategory.String:
                    expected = "a quoted string";
                    return literal.Kind == PredicateLiteralKind.String;
                default:
                    expected = "of a supported type";
                    return false;
            }
        }

        private static string Describe(RefCategory category)
        {
            return category switch
            {
                RefCategory.Bool => "bool",
                RefCategory.Enum => "an enum",
                RefCategory.Integer => "an integer",
                RefCategory.String => "a string",
                RefCategory.Double => "double/float",
                _ => "an unsupported type",
            };
        }
    }
}