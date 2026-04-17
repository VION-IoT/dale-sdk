using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;
using Google.FlatBuffers;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     A parsed MQTT message for service provider handlers. Provides pre-extracted routing
    ///     information and typed payload access, hiding the internal <see cref="MqttMessageReceived" /> type.
    /// </summary>
    [PublicApi]
    public readonly struct ServiceProviderMqttMessage
    {
        private readonly MqttMessageReceived _inner;

        internal ServiceProviderMqttMessage(MqttMessageReceived inner, ServiceProviderContractId contractId, Guid correlationId)
        {
            _inner = inner;
            ContractId = contractId;
            CorrelationId = correlationId;
        }

        /// <summary>
        ///     The service provider contract this message targets, parsed from the topic.
        /// </summary>
        public ServiceProviderContractId ContractId { get; }

        /// <summary>
        ///     The correlation ID from the MQTT message headers.
        ///     <see cref="Guid.Empty" /> if no correlation data was present or the format was not recognized.
        /// </summary>
        public Guid CorrelationId { get; }

        /// <summary>
        ///     The full MQTT topic of the received message.
        /// </summary>
        public string Topic => _inner.Topic;

        /// <summary>
        ///     The MQTT 5.0 response topic, if present. Used in request-response patterns.
        /// </summary>
        public string? ResponseTopic => _inner.ResponseTopic;

        /// <summary>
        ///     The raw payload bytes for custom deserialization.
        /// </summary>
        public ReadOnlySequence<byte> RawPayload => _inner.Payload;

        /// <summary>
        ///     Deserializes the payload as JSON.
        /// </summary>
        /// <typeparam name="T">The type to deserialize into.</typeparam>
        /// <param name="serializerOptions">
        ///     Optional JSON serializer options. If null, <see cref="JsonSerialization.DefaultOptions" /> are used.
        /// </param>
        public T GetJsonPayload<T>(JsonSerializerOptions? serializerOptions = null)
        {
            return _inner.GetJsonPayload<T>(serializerOptions);
        }

        /// <summary>
        ///     Returns the payload as a FlatBuffer ByteBuffer for deserialization.
        /// </summary>
        public ByteBuffer GetFlatBufferPayload()
        {
            return _inner.GetFlatBufferPayload();
        }

        /// <summary>
        ///     Returns the raw payload as a byte array. Use <see cref="RawPayload" /> to avoid
        ///     the copy when possible.
        /// </summary>
        public byte[] GetPayloadBytes()
        {
            var payload = _inner.Payload;
            if (payload.IsSingleSegment && MemoryMarshal.TryGetArray(payload.First, out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length)
            {
                return segment.Array;
            }

            return payload.ToArray();
        }
    }
}