using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vion.Dale.Cli.Infrastructure
{
    public static class JsonDefaults
    {
        private static JsonSerializerOptions? _options;

        public static JsonSerializerOptions Options
        {
            get => _options ??= CreateDefaultOptions();
        }

        public static JsonSerializerOptions CreateDefaultOptions()
        {
            return new JsonSerializerOptions
                   {
                       PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                       DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                       Converters = { new JsonStringEnumConverter() },
                       PropertyNameCaseInsensitive = true,
                       WriteIndented = true,
                   };
        }
    }
}