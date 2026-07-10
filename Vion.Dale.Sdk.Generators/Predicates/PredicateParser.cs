using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Vion.Dale.Sdk.Generators.Predicates
{
    /// <summary>
    ///     Self-contained recursive-descent parser for the VION presentation-predicate dialect
    ///     (<c>Presentation.VisibleWhen</c> — RFC 0017). It parses <b>and</b> shapes the AST but
    ///     <b>never evaluates</b> — strict-profile C# evaluation is RFC 0016's job. netstandard2.0,
    ///     no package dependencies (analyzers must not carry <c>Vion.Contracts</c>).
    ///     <para />
    ///     The grammar is the canonical one in <c>../vion-contracts/docs/predicates.md</c> §2.2 and is
    ///     pinned by <c>Predicates/predicate-conformance.json</c> (parse cases). Keep this file in
    ///     lock-step with that document.
    ///     <code>
    ///     predicate   := orExpr
    ///     orExpr      := andExpr ( "||" andExpr )*
    ///     andExpr     := unaryExpr ( "&amp;&amp;" unaryExpr )*
    ///     unaryExpr   := "!" negand | "(" predicate ")" | comparison | membership | boolRef
    ///     negand      := boolRef | "(" predicate ")"     // NOT a bare comparison (!A == 5 is rejected)
    ///     comparison  := ref ( "==" | "!=" | "&lt;" | "&lt;=" | "&gt;" | "&gt;=" ) literal
    ///     membership  := ref "in" "[" literal ( "," literal )* "]"
    ///     ref         := identifier | identifier "." identifier
    ///     literal     := integer | "true" | "false" | string
    ///     </code>
    /// </summary>
    internal static class PredicateParser
    {
        /// <summary>Parses <paramref name="text" /> into an AST, or returns a structured error.</summary>
        public static PredicateParseResult Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return PredicateParseResult.Fail("predicate is empty", PredicateErrorKind.Syntax);
            }

            if (!Tokenizer.TryTokenize(text!, out var tokens, out var tokenError))
            {
                return PredicateParseResult.Fail(tokenError!, PredicateErrorKind.Syntax);
            }

            var parser = new Impl(tokens);
            return parser.ParseTop();
        }

        // ── Recursive-descent implementation ──

        private sealed class Impl
        {
            private readonly IReadOnlyList<Token> _tokens;

            private int _pos;

            public Impl(IReadOnlyList<Token> tokens)
            {
                _tokens = tokens;
            }

            private Token Current => _tokens[_pos];

            public PredicateParseResult ParseTop()
            {
                PredicateNode node;
                try
                {
                    node = ParseOr();
                }
                catch (ParseException ex)
                {
                    return PredicateParseResult.Fail(ex.Message, ex.Kind);
                }

                if (Current.Kind != TokenKind.End)
                {
                    return PredicateParseResult.Fail($"unexpected '{Current.Text}' after a complete predicate", PredicateErrorKind.Syntax);
                }

                return PredicateParseResult.Ok(node);
            }

            private PredicateNode ParseOr()
            {
                var left = ParseAnd();
                while (Current.Kind == TokenKind.PipePipe)
                {
                    _pos++;
                    var right = ParseAnd();
                    left = new OrNode(left, right);
                }

                return left;
            }

            private PredicateNode ParseAnd()
            {
                var left = ParseUnary();
                while (Current.Kind == TokenKind.AmpAmp)
                {
                    _pos++;
                    var right = ParseUnary();
                    left = new AndNode(left, right);
                }

                return left;
            }

            private PredicateNode ParseUnary()
            {
                if (Current.Kind == TokenKind.Bang)
                {
                    _pos++;
                    return new NotNode(ParseNegand());
                }

                if (Current.Kind == TokenKind.LParen)
                {
                    return ParseParenthesized();
                }

                return ParseComparisonOrRef();
            }

            // negand := boolRef | "(" predicate ")"  — deliberately NOT a comparison, so "!A == 5" fails.
            private PredicateNode ParseNegand()
            {
                if (Current.Kind == TokenKind.LParen)
                {
                    return ParseParenthesized();
                }

                var reference = ParseRef();
                return new BoolRefNode(reference);
            }

            private PredicateNode ParseParenthesized()
            {
                Expect(TokenKind.LParen, "(");
                var inner = ParseOr();
                Expect(TokenKind.RParen, ")");
                return inner;
            }

            private PredicateNode ParseComparisonOrRef()
            {
                var reference = ParseRef();

                switch (Current.Kind)
                {
                    case TokenKind.EqEq:
                    case TokenKind.NotEq:
                    case TokenKind.Lt:
                    case TokenKind.Le:
                    case TokenKind.Gt:
                    case TokenKind.Ge:
                        var op = Current.Text;
                        _pos++;
                        var literal = ParseLiteral();
                        return new ComparisonNode(reference, op, literal);

                    case TokenKind.Ident when Current.Text == "in":
                        _pos++;
                        return ParseMembership(reference);

                    default:
                        return new BoolRefNode(reference);
                }
            }

            private PredicateNode ParseMembership(PredicateRef reference)
            {
                Expect(TokenKind.LBracket, "[");
                var items = new List<PredicateLiteral> { ParseLiteral() };
                while (Current.Kind == TokenKind.Comma)
                {
                    _pos++;
                    items.Add(ParseLiteral());
                }

                Expect(TokenKind.RBracket, "]");
                return new MembershipNode(reference, items);
            }

            private PredicateRef ParseRef()
            {
                var first = ExpectIdentifier();
                if (Current.Kind != TokenKind.Dot)
                {
                    return new PredicateRef(null, first);
                }

                _pos++;
                var second = ExpectIdentifier();
                if (Current.Kind == TokenKind.Dot)
                {
                    throw new ParseException("references may have at most two segments (Property or Service.Property)", PredicateErrorKind.Syntax);
                }

                return new PredicateRef(first, second);
            }

            private PredicateLiteral ParseLiteral()
            {
                switch (Current.Kind)
                {
                    case TokenKind.Int:
                        var intToken = Current;
                        _pos++;
                        return PredicateLiteral.Integer(intToken.IntValue);

                    case TokenKind.Str:
                        var strToken = Current;
                        _pos++;
                        return PredicateLiteral.String(strToken.Text);

                    case TokenKind.Ident when Current.Text == "true" || Current.Text == "false":
                        var boolText = Current.Text;
                        _pos++;
                        return PredicateLiteral.Boolean(boolText == "true");

                    case TokenKind.Ident:
                        // A bare identifier where a literal is required — the classic "unquoted enum member"
                        // (`Mode == Eco`). Routed to DALE042 (type discipline), not a generic syntax error.
                        throw new ParseException($"'{Current.Text}' is not a literal — the right side of a comparison must be a literal (quote enum/string values, e.g. 'Eco')",
                                                 PredicateErrorKind.ExpectedLiteral);

                    default:
                        throw new ParseException("expected a literal (integer, true/false, or a quoted string)", PredicateErrorKind.Syntax);
                }
            }

            private string ExpectIdentifier()
            {
                if (Current.Kind != TokenKind.Ident)
                {
                    throw new ParseException($"expected an identifier but found '{Current.Text}' (references sit on the left of a comparison)", PredicateErrorKind.Syntax);
                }

                var text = Current.Text;
                _pos++;
                return text;
            }

            private void Expect(TokenKind kind, string display)
            {
                if (Current.Kind != kind)
                {
                    throw new ParseException($"expected '{display}' but found '{Current.Text}'", PredicateErrorKind.Syntax);
                }

                _pos++;
            }
        }

        private sealed class ParseException : System.Exception
        {
            public ParseException(string message, PredicateErrorKind kind) : base(message)
            {
                Kind = kind;
            }

            public PredicateErrorKind Kind { get; }
        }

        // ── Tokenizer ──

        private enum TokenKind
        {
            Ident,
            Int,
            Str,
            Dot,
            LParen,
            RParen,
            LBracket,
            RBracket,
            Comma,
            Bang,
            EqEq,
            NotEq,
            Lt,
            Le,
            Gt,
            Ge,
            AmpAmp,
            PipePipe,
            End,
        }

        private readonly struct Token
        {
            public Token(TokenKind kind, string text, int intValue = 0)
            {
                Kind = kind;
                Text = text;
                IntValue = intValue;
            }

            public TokenKind Kind { get; }

            public string Text { get; }

            public int IntValue { get; }
        }

        private static class Tokenizer
        {
            public static bool TryTokenize(string text, out IReadOnlyList<Token> tokens, out string? error)
            {
                var list = new List<Token>();
                var i = 0;
                while (i < text.Length)
                {
                    var c = text[i];

                    if (char.IsWhiteSpace(c))
                    {
                        i++;
                        continue;
                    }

                    if (char.IsLetter(c) || c == '_')
                    {
                        var start = i;
                        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                        {
                            i++;
                        }

                        list.Add(new Token(TokenKind.Ident, text.Substring(start, i - start)));
                        continue;
                    }

                    if (char.IsDigit(c))
                    {
                        var start = i;
                        while (i < text.Length && char.IsDigit(text[i]))
                        {
                            i++;
                        }

                        var digits = text.Substring(start, i - start);
                        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                        {
                            tokens = list;
                            error = $"integer literal '{digits}' is out of the supported int32 range";
                            return false;
                        }

                        list.Add(new Token(TokenKind.Int, digits, value));
                        continue;
                    }

                    if (c == '\'' || c == '"')
                    {
                        if (!TryReadString(text, ref i, c, out var value, out error))
                        {
                            tokens = list;
                            return false;
                        }

                        list.Add(new Token(TokenKind.Str, value!));
                        continue;
                    }

                    switch (c)
                    {
                        case '.':
                            list.Add(new Token(TokenKind.Dot, "."));
                            i++;
                            continue;
                        case '(':
                            list.Add(new Token(TokenKind.LParen, "("));
                            i++;
                            continue;
                        case ')':
                            list.Add(new Token(TokenKind.RParen, ")"));
                            i++;
                            continue;
                        case '[':
                            list.Add(new Token(TokenKind.LBracket, "["));
                            i++;
                            continue;
                        case ']':
                            list.Add(new Token(TokenKind.RBracket, "]"));
                            i++;
                            continue;
                        case ',':
                            list.Add(new Token(TokenKind.Comma, ","));
                            i++;
                            continue;
                        case '!':
                            if (Peek(text, i + 1) == '=')
                            {
                                list.Add(new Token(TokenKind.NotEq, "!="));
                                i += 2;
                            }
                            else
                            {
                                list.Add(new Token(TokenKind.Bang, "!"));
                                i++;
                            }

                            continue;
                        case '=':
                            if (Peek(text, i + 1) == '=')
                            {
                                list.Add(new Token(TokenKind.EqEq, "=="));
                                i += 2;
                                continue;
                            }

                            tokens = list;
                            error = "assignment '=' is not valid; use '==' for equality";
                            return false;
                        case '<':
                            if (Peek(text, i + 1) == '=')
                            {
                                list.Add(new Token(TokenKind.Le, "<="));
                                i += 2;
                            }
                            else
                            {
                                list.Add(new Token(TokenKind.Lt, "<"));
                                i++;
                            }

                            continue;
                        case '>':
                            if (Peek(text, i + 1) == '=')
                            {
                                list.Add(new Token(TokenKind.Ge, ">="));
                                i += 2;
                            }
                            else
                            {
                                list.Add(new Token(TokenKind.Gt, ">"));
                                i++;
                            }

                            continue;
                        case '&':
                            if (Peek(text, i + 1) == '&')
                            {
                                list.Add(new Token(TokenKind.AmpAmp, "&&"));
                                i += 2;
                                continue;
                            }

                            tokens = list;
                            error = "'&' is not valid; use '&&' for logical AND";
                            return false;
                        case '|':
                            if (Peek(text, i + 1) == '|')
                            {
                                list.Add(new Token(TokenKind.PipePipe, "||"));
                                i += 2;
                                continue;
                            }

                            tokens = list;
                            error = "pipe '|' is not in the dialect; use '||' for logical OR";
                            return false;
                        default:
                            tokens = list;
                            error = $"unexpected character '{c}' (arithmetic, ternary, and function calls are not in the dialect)";
                            return false;
                    }
                }

                list.Add(new Token(TokenKind.End, "<end>"));
                tokens = list;
                error = null;
                return true;
            }

            private static bool TryReadString(string text, ref int i, char quote, out string? value, out string? error)
            {
                var sb = new StringBuilder();
                i++; // consume opening quote
                while (i < text.Length)
                {
                    var c = text[i];
                    if (c == '\\')
                    {
                        var next = Peek(text, i + 1);
                        if (next == quote)
                        {
                            sb.Append(quote);
                            i += 2;
                            continue;
                        }

                        value = null;
                        error = "string escapes beyond \\' (or \\\") are not in the dialect";
                        return false;
                    }

                    if (c == quote)
                    {
                        i++; // consume closing quote
                        value = sb.ToString();
                        error = null;
                        return true;
                    }

                    sb.Append(c);
                    i++;
                }

                value = null;
                error = "unterminated string literal";
                return false;
            }

            private static char Peek(string text, int index)
            {
                return index < text.Length ? text[index] : '\0';
            }
        }
    }
}
