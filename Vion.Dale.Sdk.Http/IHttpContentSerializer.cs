using System.Net.Http;
using System.Threading.Tasks;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     Provides serialization and deserialization functionality for HTTP content.
    /// </summary>
    public interface IHttpContentSerializer
    {
        /// <summary>
        ///     Deserializes the specified <paramref name="httpContent" /> from JSON format.
        /// </summary>
        /// <typeparam name="TContent">The type to deserialize the JSON into.</typeparam>
        /// <param name="httpContent">The <see cref="HttpContent" /> containing JSON data.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized object of type <typeparamref name="TContent" />.</returns>
        /// <exception cref="ContentNullAfterDeserializationException">Thrown when deserialization returns null.</exception>
        Task<TContent> DeserializeJsonAsync<TContent>(HttpContent httpContent)
            where TContent : notnull;

        /// <summary>
        ///     Serializes the specified <paramref name="content" /> to JSON format and sets the Content-Type header to application/json.
        /// </summary>
        /// <typeparam name="TContent">The type of the object to serialize.</typeparam>
        /// <param name="content">The object to serialize.</param>
        /// <returns><see cref="HttpContent" /> containing the serialized JSON.</returns>
        HttpContent SerializeJson<TContent>(TContent content)
            where TContent : notnull;
    }
}