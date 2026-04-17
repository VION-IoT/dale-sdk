using System;
using System.IO;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Vion.Dale.Sdk.Http
{
    /// <inheritdoc />
    internal class HttpContentSerializer : IHttpContentSerializer
    {
        private readonly JsonSerializerOptions _serializerOptions;

        public HttpContentSerializer(IOptions<JsonSerializerOptions> jsonSerializerOptions)
        {
            _serializerOptions = jsonSerializerOptions.Value;
        }

        /// <inheritdoc />
        public async Task<TContent> DeserializeJsonAsync<TContent>(HttpContent httpContent)
            where TContent : notnull
        {
            var stream = await httpContent.ReadAsStreamAsync().ConfigureAwait(false);
            var content = await JsonSerializer.DeserializeAsync<TContent>(stream, _serializerOptions).ConfigureAwait(false);

            return content ?? throw new ContentNullAfterDeserializationException(typeof(TContent));
        }

        /// <inheritdoc />
        public HttpContent SerializeJson<TContent>(TContent content)
            where TContent : notnull
        {
            var stream = new MemoryStream();
            JsonSerializer.Serialize(stream, content, _serializerOptions);
            stream.Position = 0;

            var httpContent = new StreamContent(stream);
            httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(MediaTypeNames.Application.Json);

            return httpContent;
        }
    }

    internal class ContentNullAfterDeserializationException : Exception
    {
        public ContentNullAfterDeserializationException(Type type) : base($"Content was as null after deserialization to type '{type.Name}'.")
        {
        }
    }
}