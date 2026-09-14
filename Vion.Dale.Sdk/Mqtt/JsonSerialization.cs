using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vion.Dale.Sdk.Mqtt
{
    public static class JsonSerialization
    {
        /*
         * The converter is the non-generic one because these options are a catch-all: they serve every
         * payload type a handler hands the reflection-based PublishJson/GetJsonPayload overloads, so no
         * concrete enum type is known where they are declared. JsonStringEnumConverter<TEnum> takes its
         * type as a type argument and would have to be registered once per enum, which is a list that
         * goes stale the first time a handler declares an enum nobody added here.
         *
         * The cost is that these options are not usable from a trimmed or NativeAOT consumer: the
         * non-generic converter is [RequiresDynamicCode]. That is why the hw/* payloads do not come
         * through here at all — each of their enums pins its own string form with a
         * JsonConverterAttribute on the type, and the handlers serialize through the JsonTypeInfo<T>
         * overloads against Vion.Contracts.Hw.HwJsonContext, which a NativeAOT hardware-abstraction
         * layer shares.
         *
         * NumberHandling matches that context's. Without it a non-finite double cannot cross these
         * options at all — writing one throws ArgumentException and reading `NaN` or `Infinity` throws
         * JsonException — so a payload the hw/* path round-trips would fail here, and the two halves of
         * one wire would disagree about what a value nobody can measure looks like.
         */
        public static readonly JsonSerializerOptions DefaultOptions = new()
                                                                      {
                                                                          PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                                                          DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                                                                          NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                                                                          Converters = { new JsonStringEnumConverter() },
                                                                      };
    }
}