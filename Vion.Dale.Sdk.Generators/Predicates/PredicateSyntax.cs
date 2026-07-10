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
        public PredicateNode? Ast { get; }

        public string? Error { get; }

        public PredicateErrorKind ErrorKind { get; }

        public bool IsValid
        {
            get => Error is null;
        }

        private PredicateParseResult(PredicateNode? ast, string? error, PredicateErrorKind errorKind)
        {
            Ast = ast;
            Error = error;
            ErrorKind = errorKind;
        }

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
        /// <summary>Sibling-service identifier for a qualified ref, or <c>null</c> for a bare ref.</summary>
        public string? Service { get; }

        public string Property { get; }

        public bool IsQualified
        {
            get => Service != null;
        }

        public string Text
        {
            get => IsQualified ? Service + "." + Property : Property;
        }

        public PredicateRef(string? service, string property)
        {
            Service = service;
            Property = property;
        }
    }

    internal enum PredicateLiteralKind
    {
        Integer,

        Boolean,

        String,
    }

    internal sealed class PredicateLiteral
    {
        public PredicateLiteralKind Kind { get; }

        public int IntValue { get; }

        public bool BoolValue { get; }

        public string StringValue { get; }

        private PredicateLiteral(PredicateLiteralKind kind, int intValue, bool boolValue, string stringValue)
        {
            Kind = kind;
            IntValue = intValue;
            BoolValue = boolValue;
            StringValue = stringValue;
        }

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
        public PredicateNode Left { get; }

        public PredicateNode Right { get; }

        public OrNode(PredicateNode left, PredicateNode right)
        {
            Left = left;
            Right = right;
        }
    }

    internal sealed class AndNode : PredicateNode
    {
        public PredicateNode Left { get; }

        public PredicateNode Right { get; }

        public AndNode(PredicateNode left, PredicateNode right)
        {
            Left = left;
            Right = right;
        }
    }

    internal sealed class NotNode : PredicateNode
    {
        public PredicateNode Operand { get; }

        public NotNode(PredicateNode operand)
        {
            Operand = operand;
        }
    }

    internal sealed class ComparisonNode : PredicateNode
    {
        public PredicateRef Reference { get; }

        public string Operator { get; }

        public PredicateLiteral Literal { get; }

        public bool IsRelational
        {
            get => Operator is "<" or "<=" or ">" or ">=";
        }

        public ComparisonNode(PredicateRef reference, string op, PredicateLiteral literal)
        {
            Reference = reference;
            Operator = op;
            Literal = literal;
        }
    }

    internal sealed class MembershipNode : PredicateNode
    {
        public PredicateRef Reference { get; }

        public IReadOnlyList<PredicateLiteral> Items { get; }

        public MembershipNode(PredicateRef reference, IReadOnlyList<PredicateLiteral> items)
        {
            Reference = reference;
            Items = items;
        }
    }

    internal sealed class BoolRefNode : PredicateNode
    {
        public PredicateRef Reference { get; }

        public BoolRefNode(PredicateRef reference)
        {
            Reference = reference;
        }
    }
}