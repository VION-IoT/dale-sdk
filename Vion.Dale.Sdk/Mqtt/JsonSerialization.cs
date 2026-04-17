using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vion.Dale.Sdk.Mqtt
{
    public static class JsonSerialization
    {
        public static readonly JsonSerializerOptions DefaultOptions = new()
                                                                      {
                                                                          PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                                                          DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                                                                          Converters = { new JsonStringEnumConverter() },
                                                                      };
    }
}