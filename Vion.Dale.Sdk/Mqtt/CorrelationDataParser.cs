using System;
using System.Buffers.Text;

namespace Vion.Dale.Sdk.Mqtt
{
    public static class CorrelationDataParser
    {
        /// <summary>
        ///     Tries to retrieve the correlation ID from the correlation data.
        /// </summary>
        /// <param name="correlationData">The raw correlation data byte array to parse as a <see cref="Guid" />.</param>
        /// <returns>
        ///     The extracted correlation ID as a <see cref="Guid" />, or <see cref="Guid.Empty" /> if the correlation data is null
        ///     or in an unrecognized format.
        /// </returns>
        /// <remarks>
        ///     Supports 16-byte binary GUIDs and 36-character UTF-8 string GUIDs.
        /// </remarks>
        public static Guid TryGetCorrelationId(byte[] correlationData)
        {
            return correlationData.Length switch
            {
                16 => new Guid(correlationData),
                36 when Utf8Parser.TryParse(correlationData, out Guid correlationId, out _) => correlationId,
                _ => Guid.Empty,
            };
        }
    }
}
