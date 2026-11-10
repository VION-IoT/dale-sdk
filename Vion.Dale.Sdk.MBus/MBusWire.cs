using System.Text.Json.Serialization;

// ReSharper disable PartialTypeWithSinglePart this must be a partial class because it is generated

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>The topic segments of the M-Bus contract, under the contract's base topic.</summary>
    internal static class MBusTopics
    {
        /// <summary>The routing key the handler registers under.</summary>
        public const string MBus = "/hw/mbus";

        /// <summary>The read action.</summary>
        public const string Get = MBus + "/get";
    }

    /// <summary>
    ///     Requests a read, published by dale on <see cref="MBusTopics.Get" /> with an MQTT 5 response topic,
    ///     correlation data and message expiry.
    /// </summary>
    /// <param name="PrimaryAddress">The meter's primary address, 0–250.</param>
    /// <param name="MaxTelegrams">How many telegrams the meter sends, so how many the provider reads at most.</param>
    internal sealed record GetMBusPayload(int PrimaryAddress, int MaxTelegrams);

    /// <summary>
    ///     Answers a <see cref="GetMBusPayload" /> on the request's response topic.
    /// </summary>
    /// <param name="ResponseCode">
    ///     <c>Ok</c>, <c>NoResponse</c>, <c>Garbled</c>, <c>LineUnavailable</c> or <c>NotSupported</c>; a later contract
    ///     version may append others, which this SDK reads as a version mismatch.
    /// </param>
    /// <param name="ErrorMessage">A human-readable diagnostic, or <c>null</c>.</param>
    /// <param name="Telegrams">
    ///     Every RSP_UD long frame as received, from 68h to 16h inclusive, each in base64; only with
    ///     <c>Ok</c>.
    /// </param>
    internal sealed record GetMBusResponsePayload(string ResponseCode, string? ErrorMessage, byte[][]? Telegrams);

    /// <summary>
    ///     The source-generated serialization of the M-Bus wire records: camelCase names, byte arrays in base64. The
    ///     response code travels as a name and is read as a string, so a code newer than this SDK is not a
    ///     deserialization failure.
    /// </summary>
    [JsonSerializable(typeof(GetMBusPayload))]
    [JsonSerializable(typeof(GetMBusResponsePayload))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase)]
    internal partial class MBusJsonContext : JsonSerializerContext;
}