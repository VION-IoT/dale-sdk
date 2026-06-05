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
    ///     forms. The framework applies this to <c>TimeSpan?</c> automatically via its nullable wrapper.
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
                return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
            }
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(XmlConvert.ToString(value));
        }
    }
}