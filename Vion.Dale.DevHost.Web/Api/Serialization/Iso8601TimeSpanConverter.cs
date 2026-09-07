using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace Vion.Dale.DevHost.Web.Api.Serialization
{
    /// <summary>
    ///     Serializes <see cref="TimeSpan" /> as an ISO-8601 duration ("PT5S") — the rich-types Duration wire
    ///     form the codec (<c>PropertyValueCodec</c>) and the MQTT runtime use. Without this, System.Text.Json
    ///     emits the .NET ToString form ("00:00:05"), which diverges from the codec the write path decodes with
    ///     and which agents reading the HTTP/SignalR surface should not have to special-case. Read accepts
    ///     both forms, reads an absent value as zero, and refuses anything else as a malformed payload.
    ///     The framework applies this to <c>TimeSpan?</c> automatically via its nullable wrapper.
    /// </summary>
    public sealed class Iso8601TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (string.IsNullOrEmpty(text))
            {
                return TimeSpan.Zero;
            }

            try
            {
                return XmlConvert.ToTimeSpan(text);
            }
            catch (FormatException)
            {
                // Not the ISO form; the .NET form is the other one this converter accepts.
            }
            catch (OverflowException)
            {
                // A well-formed ISO duration naming a span TimeSpan cannot hold — "P100000000D".
                // Caught beside the format failure because the caller's repair is the same, and
                // because leaving it uncaught is the defect this converter was fixed for.
            }

            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            // JsonException is the class the input pipeline reads as a malformed payload: on the
            // request-response wire that is the 400 of the spec's refusal shapes. Any other class
            // escapes the pipeline untranslated, which on that wire is a 500.
            throw new JsonException($"'{text}' is neither an ISO-8601 duration (\"PT5S\") nor a .NET duration (\"00:00:05\"), or names a span that cannot be represented.");
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(XmlConvert.ToString(value));
        }
    }
}