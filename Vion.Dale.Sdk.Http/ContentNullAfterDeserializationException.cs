using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     Thrown when a response body was well-formed JSON that deserialized to null — the literal
    ///     <c>null</c> document — rather than to the type a member asked for.
    /// </summary>
    /// <remarks>
    ///     It is distinct from the <c>JsonException</c> a body that is absent, truncated or not JSON at all
    ///     produces, and it is the only way to tell "the server answered null" from "the server answered
    ///     rubbish" without matching on message text.
    /// </remarks>
    [PublicApi]
    public class ContentNullAfterDeserializationException : Exception
    {
        /// <summary>
        ///     Gets the type the response body could not be deserialized into.
        /// </summary>
        public Type Type { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentNullAfterDeserializationException" /> class.
        /// </summary>
        /// <param name="type">The type the response body could not be deserialized into.</param>
        public ContentNullAfterDeserializationException(Type type) : base($"Content was null after deserialization to type '{type.FullName}'.")
        {
            Type = type;
        }
    }
}