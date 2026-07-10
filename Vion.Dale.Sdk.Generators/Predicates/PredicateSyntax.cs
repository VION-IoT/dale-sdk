using System.Collections.Generic;

namespace Vion.Dale.Sdk.Generators.Predicates
{
    /// <summary>How a parse failed — routes to the right diagnostic (DALE041 vs DALE042).</summary>
    internal enum PredicateErrorKind
    {
        /// <summary>Structural / grammar failure → DALE041.</summary>
        Syntax,

        /// <summary>A bare identifier where a literal was required — the "unquoted enum member" case → DALE042.</summary>
        ExpectedLiteral,
    }

    /// <summary>Result of <see cref="PredicateParser.Parse" />: either an AST or a structured error.</summary>
    internal sealed class PredicateParseResult
    {
        private PredicateParseResult(PredicateNode? ast, string? error, PredicateErrorKind errorKind)
        {
            Ast = ast;
            Error = error;
            ErrorKind = errorKind;
        }

        public PredicateNode? Ast { get; }

        public string? Error { get; }

        public PredicateErrorKind ErrorKind { get; }

        public bool IsValid => Error is null;

        public static PredicateParseResult Ok(PredicateNode ast)
        {
            return new PredicateParseResult(ast, null, PredicateErrorKind.Syntax);
        }

        public static PredicateParseResult Fail(string error, PredicateErrorKind kind)
        {
            return new PredicateParseResult(null, error, kind);
        }
    }

    /// <summary>A reference: a bare property (<c>Service</c> null) or a two-segment <c>Service.Property</c>.</summary>
    internal sealed class PredicateRef
    {
        public PredicateRef(string? service, string property)
        {
            Service = service;
            Property = property;
        }

        /// <summary>Sibling-service identifier for a qualified ref, or <c>null</c> for a bare ref.</summary>
        public string? Service { get; }

        public string Property { get; }

        public bool IsQualified => Service != null;

        public string Text => IsQualified ? Service + "." + Property : Property;
    }

    internal enum PredicateLiteralKind
    {
        Integer,
        Boolean,
        String,
    }

    internal sealed class PredicateLiteral
    {
        private PredicateLiteral(PredicateLiteralKind kind, int intValue, bool boolValue, string stringValue)
        {
            Kind = kind;
            IntValue = intValue;
            BoolValue = boolValue;
            StringValue = stringValue;
        }

        public PredicateLiteralKind Kind { get; }

        public int IntValue { get; }

        public bool BoolValue { get; }

        public string StringValue { get; }

        public static PredicateLiteral Integer(int value)
        {
            return new PredicateLiteral(PredicateLiteralKind.Integer, value, false, string.Empty);
        }

        public static PredicateLiteral Boolean(bool value)
        {
            return new PredicateLiteral(PredicateLiteralKind.Boolean, 0, value, string.Empty);
        }

        public static PredicateLiteral String(string value)
        {
            return new PredicateLiteral(PredicateLiteralKind.String, 0, false, value);
        }
    }

    // ── AST nodes ──

    internal abstract class PredicateNode
    {
    }

    internal sealed class OrNode : PredicateNode
    {
        public OrNode(PredicateNode left, PredicateNode right)
        {
            Left = left;
            Right = right;
        }

        public PredicateNode Left { get; }

        public PredicateNode Right { get; }
    }

    internal sealed class AndNode : PredicateNode
    {
        public AndNode(PredicateNode left, PredicateNode right)
        {
            Left = left;
            Right = right;
        }

        public PredicateNode Left { get; }

        public PredicateNode Right { get; }
    }

    internal sealed class NotNode : PredicateNode
    {
        public NotNode(PredicateNode operand)
        {
            Operand = operand;
        }

        public PredicateNode Operand { get; }
    }

    internal sealed class ComparisonNode : PredicateNode
    {
        public ComparisonNode(PredicateRef reference, string op, PredicateLiteral literal)
        {
            Reference = reference;
            Operator = op;
            Literal = literal;
        }

        public PredicateRef Reference { get; }

        public string Operator { get; }

        public PredicateLiteral Literal { get; }

        public bool IsRelational => Operator is "<" or "<=" or ">" or ">=";
    }

    internal sealed class MembershipNode : PredicateNode
    {
        public MembershipNode(PredicateRef reference, IReadOnlyList<PredicateLiteral> items)
        {
            Reference = reference;
            Items = items;
        }

        public PredicateRef Reference { get; }

        public IReadOnlyList<PredicateLiteral> Items { get; }
    }

    internal sealed class BoolRefNode : PredicateNode
    {
        public BoolRefNode(PredicateRef reference)
        {
            Reference = reference;
        }

        public PredicateRef Reference { get; }
    }
}
