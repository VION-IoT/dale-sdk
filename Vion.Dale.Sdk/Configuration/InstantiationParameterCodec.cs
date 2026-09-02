using System.Reflection;
using System.Text.Json.Nodes;
using Vion.Contracts.Codec;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.Sdk.Configuration
{
    /// <summary>
    ///     The one decode rule for an <c>[InstantiationParameter]</c> value, so a host that checks a
    ///     configuration before sending it and the block that applies it cannot disagree about what decodes.
    ///     A development host pre-checks a topology's values where the operator is; the block is still the
    ///     fail-closed authority, and both arrive at the same verdict because both call this.
    /// </summary>
    internal static class InstantiationParameterCodec
    {
        /// <summary>
        ///     Decodes <paramref name="value" /> into <paramref name="property" />'s CLR type using the schema
        ///     built from the property itself — binder output does not exist before binding. Throws the codec's
        ///     own exception, whose message names the rule that refused the value.
        /// </summary>
        public static object? Decode(PropertyInfo property, JsonNode? value)
        {
            return PropertyValueCodec.JsonToClr(value, TypeRefBuilder.BuildForProperty(property), property.PropertyType);
        }
    }
}