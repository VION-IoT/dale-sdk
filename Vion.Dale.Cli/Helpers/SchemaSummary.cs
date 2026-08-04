using System.Linq;
using System.Text.Json.Nodes;

namespace Vion.Dale.Cli.Helpers
{
    /// <summary>
    ///     Derives the compact type label `dale list` shows for a service property or measuring
    ///     point from its introspection schema (JSON Schema 2020-12, Dale profile).
    ///     Identity rule: <c>title</c> names the CLR type only on enum/struct schemas; on
    ///     primitives it is a display name and must be ignored.
    /// </summary>
    internal static class SchemaSummary
    {
        public static string Describe(JsonNode? schema)
        {
            if (schema is not JsonObject schemaObject)
            {
                return string.Empty;
            }

            var (typeName, isNullable) = ReadType(schemaObject);
            var suffix = isNullable ? "?" : string.Empty;

            if (schemaObject.ContainsKey("enum"))
            {
                return (ReadString(schemaObject, "title") ?? "enum") + suffix;
            }

            switch (typeName)
            {
                case null:
                    return string.Empty;
                case "object":
                    return (ReadString(schemaObject, "title") ?? "object") + suffix;
                case "array":
                    var itemSummary = Describe(schemaObject["items"]);
                    return (itemSummary.Length > 0 ? itemSummary + "[]" : "array") + suffix;
                default:
                    return (ReadString(schemaObject, "format") ?? typeName) + suffix;
            }
        }

        private static (string? TypeName, bool IsNullable) ReadType(JsonObject schemaObject)
        {
            switch (schemaObject["type"])
            {
                case JsonValue value when value.TryGetValue<string>(out var single):
                    return (single, false);
                case JsonArray array:
                    var names = array.Select(node => node is JsonValue value && value.TryGetValue<string>(out var name) ? name : null).Where(name => name != null).ToList();
                    return (names.FirstOrDefault(name => name != "null"), names.Contains("null"));
                default:
                    return (null, false);
            }
        }

        private static string? ReadString(JsonObject schemaObject, string key)
        {
            return schemaObject[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
    }
}