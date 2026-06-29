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
            var isNullableRef = IsNullableReferenceType(property);
            return Build(property.PropertyType, isNullableRef);
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
                              Minimum = !double.IsNegativeInfinity(sf.Minimum) ? sf.Minimum : null,
                              Maximum = !double.IsPositiveInfinity(sf.Maximum) ? sf.Maximum : null,
                          };

                if (!ann.IsEmpty)
                {
                    builder[ToCamelCase(p.Name!)] = ann;
                }
            }

            return builder.ToImmutable();
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

        private static TypeRef Build(Type type, bool isNullableRef)
        {
            // Nullable<T> for value types — structural (e.g. int?, double?).
            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                // The inner value type is never a nullable reference, so pass false.
                return new NullableTypeRef(Build(underlying, false));
            }

            // ImmutableArray<T>
            if (IsImmutableArray(type, out var elementType))
            {
                // Array element nullability: not tracked at property level for netstandard2.1.
                // Struct parameters use their own NullabilityInfo via BuildStructTypeRef.
                return new ArrayTypeRef(Build(elementType!, false));
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
                var isNullableRef = IsNullableReferenceType(p);
                var isNullableValue = Nullable.GetUnderlyingType(p.ParameterType) is not null;

                var fieldRef = Build(p.ParameterType, isNullableRef);
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
        ///     Peels <c>Nullable&lt;T&gt;</c> and <c>ImmutableArray&lt;T&gt;</c> wrappers recursively
        ///     until a base type is reached. Returns the type if it is a readonly record struct,
        ///     otherwise returns <c>null</c>.
        /// </summary>
        private static Type? ExtractStructType(Type t)
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
        ///     Detects whether a property is declared as a nullable reference type (e.g. <c>string?</c>) by
        ///     reading the compiler-emitted <c>[Nullable(2)]</c> attribute, falling back to
        ///     <c>[NullableContext]</c> on the declaring type. Only applicable to reference types; value types
        ///     use <c>Nullable&lt;T&gt;</c> instead.
        /// </summary>
        private static bool IsNullableReferenceType(PropertyInfo property)
        {
            if (property.PropertyType.IsValueType)
            {
                // Value-type nullability is handled via Nullable<T> — not via the attribute.
                return false;
            }

            return ReadNullableAnnotation(property.GetCustomAttributes(false)) ??
                   (property.DeclaringType is { } declaring ? ReadNullableContext(declaring.GetCustomAttributes(false)) : null) ?? false;
        }

        /// <summary>
        ///     Detects whether a positional record-struct field (a primary-constructor parameter) is a nullable
        ///     reference type (e.g. <c>string?</c>). The compiler emits <c>[Nullable]</c> on the parameter; when
        ///     the whole constructor / type shares one nullability it emits <c>[NullableContext]</c> on the
        ///     constructor or declaring type instead, so both fallbacks are consulted. Only applicable to
        ///     reference types; value-type fields use <c>Nullable&lt;T&gt;</c>.
        /// </summary>
        private static bool IsNullableReferenceType(ParameterInfo parameter)
        {
            if (parameter.ParameterType.IsValueType)
            {
                return false;
            }

            return ReadNullableAnnotation(parameter.GetCustomAttributes(false)) ?? ReadNullableContext(parameter.Member.GetCustomAttributes(false)) ??
                   (parameter.Member.DeclaringType is { } declaring ? ReadNullableContext(declaring.GetCustomAttributes(false)) : null) ?? false;
        }

        /// <summary>
        ///     Reads the compiler-emitted <c>[Nullable(b)]</c> flag from a member's own attributes:
        ///     <c>true</c> = annotated (may be null), <c>false</c> = not annotated, <c>null</c> = no
        ///     <c>[Nullable]</c> attribute present (so the caller falls back to <c>[NullableContext]</c>).
        /// </summary>
        private static bool? ReadNullableAnnotation(object[] attributes)
        {
            var nullable = attributes.FirstOrDefault(a => a.GetType().FullName == "System.Runtime.CompilerServices.NullableAttribute");
            if (nullable is not null && nullable.GetType().GetField("NullableFlags")?.GetValue(nullable) is byte[] bytes && bytes.Length > 0)
            {
                return bytes[0] == 2; // 2 = annotated (may be null), 1 = not annotated.
            }

            return null;
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
    }
}