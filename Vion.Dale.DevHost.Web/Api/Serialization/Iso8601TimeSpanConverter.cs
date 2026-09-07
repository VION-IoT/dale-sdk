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
    ///     and which agents reading the HTTP/SignalR surface should not have to special-case. Read accepts both
    ///     forms and refuses anything else as a malformed payload. The framework applies this to
    ///     <c>TimeSpan?</c> automatically via its nullable wrapper.
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

            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            // JsonException is the class the input pipeline reads as a malformed body and answers 400
            // with; a parse exception escaping here is an unhandled fault and reaches the caller as 500.
            throw new JsonException($"'{text}' is neither an ISO-8601 duration (\"PT5S\") nor a .NET duration (\"00:00:05\").");
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(XmlConvert.ToString(value));
        }
    }
}