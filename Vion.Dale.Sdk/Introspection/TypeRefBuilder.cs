using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Introspection
{
    /// <summary>
    ///     Builds a <see cref="TypeRef" /> tree from a CLR <see cref="Type" /> reflected from a
    ///     property. Used by <see cref="LogicBlockIntrospection" /> when emitting the per-property
    ///     schema document. Nullability of reference types (specifically <c>string?</c> vs <c>string</c>)
    ///     is detected by reading the compiler-emitted <c>[Nullable]</c> attribute, which is present
    ///     when <c>&lt;Nullable&gt;enable&lt;/Nullable&gt;</c> is set in the project.
    /// </summary>
    internal static class TypeRefBuilder
    {
        /// <summary>
        ///     Builds a <see cref="TypeRef" /> for a property declared on a CLR type.
        /// </summary>
        public static TypeRef BuildForProperty(PropertyInfo property)
        {
            return Build(property.PropertyType, NullabilityOf(property));
        }

        /// <summary>
        ///     Builds struct-field annotations for a property whose CLR type is (or contains) a
        ///     flat <c>readonly record struct</c>. Returns an empty dictionary for non-struct properties.
        /// </summary>
        public static ImmutableDictionary<string, TypeAnnotations> BuildStructFieldAnnotations(Type propertyType)
        {
            var structType = ExtractStructType(propertyType);
            if (structType is null)
            {
                return ImmutableDictionary<string, TypeAnnotations>.Empty;
            }

            var ctor = structType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault(c => c.GetParameters().Length > 0);

            if (ctor is null)
            {
                return ImmutableDictionary<string, TypeAnnotations>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<string, TypeAnnotations>();
            foreach (var p in ctor.GetParameters())
            {
                var sf = p.GetCustomAttribute<StructFieldAttribute>();
                if (sf is null)
                {
                    continue;
                }

                var ann = new TypeAnnotations
                          {
                              Title = sf.Title,
                              Description = sf.Description,
                              Unit = sf.Unit,
                              Format = sf.StringFormat,

                              // Same rule as a property's bounds: a bound the wire cannot carry is not a bound.
                              Minimum = FiniteBound(sf.Minimum),
                              Maximum = FiniteBound(sf.Maximum),
                              WriteOnly = sf.WriteOnly,
                          };

                if (!ann.IsEmpty)
                {
                    builder[ToCamelCase(p.Name!)] = ann;
                }
            }

            return builder.ToImmutable();
        }

        /// <summary>
        ///     Builds the <see cref="TypeRef" /> of a single positional struct field, honouring the
        ///     parameter's nullable-reference annotation exactly as <see cref="BuildStructTypeRef" /> does.
        ///     Exposed so callers that reason about a field's wire schema — notably
        ///     <see cref="StructFieldPresentationBuilder" />, which must know whether the field's
        ///     <c>title</c> slot is identity-bearing — need not re-derive it.
        /// </summary>
        internal static TypeRef BuildForStructField(ParameterInfo parameter)
        {
            return Build(parameter.ParameterType, NullabilityOf(parameter));
        }

        /// <summary>
        ///     Returns <c>true</c> when <paramref name="type" /> is a <c>readonly record struct</c>.
        ///     The C# compiler emits <c>[IsReadOnlyAttribute]</c> on every <c>readonly struct</c>.
        ///     For the record part, older compilers (pre-C# 13 / .NET &lt; 10) emit a
        ///     <c>&lt;Clone&gt;$</c> method; the C# 13 / .NET 10 compiler dropped that method and
        ///     emits a <c>PrintMembers(StringBuilder)</c> method instead (which plain structs never
        ///     receive). We check both to handle assemblies compiled by either toolchain.
        /// </summary>
        internal static bool IsReadonlyRecordStruct(Type type)
        {
            if (!type.IsValueType)
            {
                return false;
            }

            var hasIsReadOnly = type.GetCustomAttributes(false).Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");

            if (!hasIsReadOnly)
            {
                return false;
            }

            // Older compilers emit <Clone>$ as a record-struct marker.
            var hasClone = type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;

            // C# 13 / .NET 10 dropped <Clone>$. Record structs always emit PrintMembers(StringBuilder);
            // plain (non-record) structs never receive this method.
            var hasPrintMembers = type.GetMethod("PrintMembers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

            return hasClone || hasPrintMembers;
        }

        /// <summary>
        ///     Converts a PascalCase or already-camelCase name to camelCase.
        ///     Used to produce JSON field names that match the wire convention (spec §5.4.2).
        /// </summary>
        internal static string ToCamelCase(string s)
        {
            if (string.IsNullOrEmpty(s) || char.IsLower(s[0]))
            {
                return s;
            }

            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>
        ///     Peels <c>Nullable&lt;T&gt;</c> and <c>ImmutableArray&lt;T&gt;</c> wrappers recursively
        ///     until a base type is reached. Returns the type if it is a readonly record struct,
        ///     otherwise returns <c>null</c>.
        /// </summary>
        internal static Type? ExtractStructType(Type t)
        {
            while (true)
            {
                var u = Nullable.GetUnderlyingType(t);
                if (u is not null)
                {
                    t = u;
                    continue;
                }

                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
                {
                    t = t.GetGenericArguments()[0];
                    continue;
                }

                if (t.IsValueType && IsReadonlyRecordStruct(t))
                {
                    return t;
                }

                return null;
            }
        }

        /// <summary>
        ///     A declared struct-field bound, or <c>null</c> where it is not a number the wire can carry —
        ///     the field-level half of the rule <c>PropertyMetadataBuilder.FiniteBound</c> applies to a
        ///     property's own bounds, and for the same reason.
        /// </summary>
        private static double? FiniteBound(double declared)
        {
            return !double.IsNaN(declared) && !double.IsInfinity(declared) ? declared : null;
        }

        /// <summary>
        ///     Builds the reference for one position of a member's type, consuming that position's nullability
        ///     flag from <paramref name="nullability" /> before recursing — so the walk of the type and the
        ///     walk of the flags stay in step at any nesting depth.
        /// </summary>
        private static TypeRef Build(Type type, NullabilityWalk nullability)
        {
            var isNullableRef = nullability.NextPosition();

            // Nullable<T> for value types — structural (e.g. int?, double?).
            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                return new NullableTypeRef(Build(underlying, nullability));
            }

            // ImmutableArray<T>
            if (IsImmutableArray(type, out var elementType))
            {
                // The element is the next position of the same walk, so ImmutableArray<string?> reaches the
                // wire as a nullable item — without it the outbound codec refuses a null element and drops
                // the whole publish, which is the failure the member-level rule already avoids.
                return new ArrayTypeRef(Build(elementType!, nullability));
            }

            // string — honour nullable annotation (string? → NullableTypeRef(String)).
            if (type == typeof(string))
            {
                var stringRef = new PrimitiveTypeRef(PrimitiveKind.String);
                return isNullableRef ? new NullableTypeRef(stringRef) : stringRef;
            }

            // Primitive value types
            var primitive = TryMapPrimitive(type);
            if (primitive is not null)
            {
                return new PrimitiveTypeRef(primitive.Value);
            }

            // Enum — member name strings (no integer values on the wire per spec §5.1).
            if (type.IsEnum)
            {
                return new EnumTypeRef(type.Name, ImmutableArray.CreateRange(Enum.GetNames(type)));
            }

            // Readonly record struct — flat fields enumerated via the primary positional constructor.
            if (type.IsValueType && IsReadonlyRecordStruct(type))
            {
                return BuildStructTypeRef(type);
            }

            throw new NotSupportedException($"Type '{type.FullName}' is not a supported service-element type. " +
                                            "Use a primitive, nullable primitive, enum, ImmutableArray<T>, or readonly record struct. " +
                                            "See DALE003 / DALE016 for the full whitelist.");
        }

        private static PrimitiveKind? TryMapPrimitive(Type type)
        {
            if (type == typeof(bool))
            {
                return PrimitiveKind.Bool;
            }

            if (type == typeof(byte))
            {
                return PrimitiveKind.Byte;
            }

            if (type == typeof(short))
            {
                return PrimitiveKind.Short;
            }

            if (type == typeof(ushort))
            {
                return PrimitiveKind.UShort;
            }

            if (type == typeof(int))
            {
                return PrimitiveKind.Int;
            }

            if (type == typeof(uint))
            {
                return PrimitiveKind.UInt;
            }

            if (type == typeof(long))
            {
                return PrimitiveKind.Long;
            }

            if (type == typeof(float))
            {
                return PrimitiveKind.Float;
            }

            if (type == typeof(double))
            {
                return PrimitiveKind.Double;
            }

            if (type == typeof(DateTime))
            {
                return PrimitiveKind.DateTime;
            }

            if (type == typeof(TimeSpan))
            {
                return PrimitiveKind.Duration;
            }

            if (type == typeof(Guid))
            {
                return PrimitiveKind.Guid;
            }

            return null;
        }

        private static bool IsImmutableArray(Type type, out Type? elementType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }

            elementType = null;
            return false;
        }

        private static StructTypeRef BuildStructTypeRef(Type structType)
        {
            // Use the primary positional constructor (most parameters) to enumerate fields
            // in declaration order — the compiler guarantees parameter order matches declaration order.
            var ctor = structType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault(c => c.GetParameters().Length > 0) ??
                       throw new NotSupportedException($"Struct '{structType.FullName}' has no positional constructor. " +
                                                       "Only positional readonly record structs are supported as service-element types.");

            var fieldsBuilder = ImmutableArray.CreateBuilder<StructField>();
            var requiredBuilder = ImmutableArray.CreateBuilder<string>();

            foreach (var p in ctor.GetParameters())
            {
                // A struct field is nullable when it is a Nullable<T> value type (double?, DateTime?, an
                // enum?) or a nullable reference type (string?). Build already wraps Nullable<T>; the
                // reference-type case needs the parameter's [Nullable] annotation threaded through so string?
                // becomes NullableTypeRef(String) rather than a bare (non-nullable) string — otherwise the
                // outbound codec throws on a null field and dale drops the whole property publish. DALE003
                // permits these nullable fields (see AnalyzerHelper.IsFlatReadonlyRecordStruct).
                var nullability = NullabilityOf(p);
                var isNullableRef = nullability.Peek();
                var isNullableValue = Nullable.GetUnderlyingType(p.ParameterType) is not null;

                var fieldRef = Build(p.ParameterType, nullability);
                var camelName = ToCamelCase(p.Name!);
                fieldsBuilder.Add(new StructField(camelName, fieldRef));

                // Only non-nullable fields are required: a nullable field encodes as JSON null outbound and
                // may be omitted inbound, so listing it in Required would reject a legitimately-absent value.
                if (!isNullableRef && !isNullableValue)
                {
                    requiredBuilder.Add(camelName);
                }
            }

            return new StructTypeRef(structType.Name, fieldsBuilder.ToImmutable(), requiredBuilder.ToImmutable());
        }

        /// <summary>
        ///     The nullability flags a property carries, as a walk. <c>netstandard2.1</c> has no
        ///     <c>NullabilityInfoContext</c>, so the compiler-emitted attribute is read directly; the fallback
        ///     is <c>[NullableContext]</c> on the declaring type, which states one flag for every position.
        /// </summary>
        private static NullabilityWalk NullabilityOf(PropertyInfo property)
        {
            return new NullabilityWalk(ReadNullableFlags(property.GetCustomAttributes(false)),
                                       property.DeclaringType is { } declaring ? ReadNullableContext(declaring.GetCustomAttributes(false)) : null);
        }

        /// <summary>
        ///     The same walk for a positional record-struct field. The compiler emits <c>[Nullable]</c> on the
        ///     parameter; where the whole constructor or type shares one nullability it emits
        ///     <c>[NullableContext]</c> on the constructor or the declaring type instead, so both fallbacks are
        ///     consulted in that order.
        /// </summary>
        private static NullabilityWalk NullabilityOf(ParameterInfo parameter)
        {
            return new NullabilityWalk(ReadNullableFlags(parameter.GetCustomAttributes(false)),
                                       ReadNullableContext(parameter.Member.GetCustomAttributes(false)) ??
                                       (parameter.Member.DeclaringType is { } declaring ? ReadNullableContext(declaring.GetCustomAttributes(false)) : null));
        }

        /// <summary>
        ///     Reads the compiler-emitted <c>[Nullable(…)]</c> flags from a member's own attributes, or
        ///     <c>null</c> where the attribute is absent. The compiler emits <b>one flag per position</b> of a
        ///     pre-order walk of the member's type — the member itself, then each type argument in turn — and
        ///     collapses the array to a single flag where every position agrees.
        /// </summary>
        private static byte[]? ReadNullableFlags(object[] attributes)
        {
            var nullable = attributes.FirstOrDefault(a => a.GetType().FullName == "System.Runtime.CompilerServices.NullableAttribute");
            return nullable?.GetType().GetField("NullableFlags")?.GetValue(nullable) as byte[];
        }

        /// <summary>
        ///     Reads the <c>[NullableContext(b)]</c> fallback flag the compiler emits on a member / type when all
        ///     its members share one nullability. Returns <c>null</c> when no context attribute is present.
        /// </summary>
        private static bool? ReadNullableContext(object[] attributes)
        {
            var ctx = attributes.FirstOrDefault(a => a.GetType().FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
            if (ctx is not null && ctx.GetType().GetField("Flag")?.GetValue(ctx) is byte b)
            {
                return b == 2;
            }

            return null;
        }

        /// <summary>
        ///     Hands out one nullability flag per position of a type walk, in the order the compiler emitted
        ///     them. A single-flag attribute states the same answer for every position, and an absent one
        ///     defers to the declaring context — so a caller never has to know which of the three shapes it
        ///     got. <c>2</c> is the annotated flag; anything else, and any position past the end, is not.
        /// </summary>
        private sealed class NullabilityWalk
        {
            private readonly bool? _context;

            private readonly byte[]? _flags;

            private int _position;

            public NullabilityWalk(byte[]? flags, bool? context)
            {
                _flags = flags is { Length: > 0 } ? flags : null;
                _context = context;
            }

            /// <summary>The flag for the next position, consuming it.</summary>
            public bool NextPosition()
            {
                var annotated = Peek();
                _position++;
                return annotated;
            }

            /// <summary>The flag for the next position, leaving it for <see cref="NextPosition" />.</summary>
            public bool Peek()
            {
                if (_flags is null)
                {
                    return _context ?? false;
                }

                // One flag stands for every position; past the end, nothing is annotated.
                var index = _flags.Length == 1 ? 0 : _position;
                return index < _flags.Length && _flags[index] == 2;
            }
        }
    }
}