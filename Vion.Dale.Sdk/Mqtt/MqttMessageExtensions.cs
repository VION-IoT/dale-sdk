using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Vion.Dale.Sdk.Utils;
using Google.FlatBuffers;

namespace Vion.Dale.Sdk.Mqtt
{
    /// <summary>
    ///     Extension methods for <see cref="MqttMessageReceived" /> to simplify payload processing and topic parsing.
    /// </summary>
    public static class MqttMessageExtensions
    {
        extension(MqttMessageReceived message)
        {
            /// <summary>
            ///     Deserializes the MQTT message payload as JSON.
            /// </summary>
            /// <typeparam name="T">The type to deserialize the JSON payload into.</typeparam>
            /// <param name="serializerOptions">Optional JSON serializer options. If null, <see cref="JsonSerialization.DefaultOptions" /> are used.</param>
            /// <returns>The deserialized object of type T.</returns>
            /// <exception cref="InvalidOperationException">Thrown when deserialization returns null.</exception>
            public T GetJsonPayload<T>(JsonSerializerOptions? serializerOptions = null)
            {
                serializerOptions ??= JsonSerialization.DefaultOptions;
                var payload = message.Payload;
                if (payload.IsSingleSegment)
                {
                    return JsonSerializer.Deserialize<T>(payload.FirstSpan, serializerOptions) ??
                           throw new InvalidOperationException("Deserialization of SingleSegment payload returned null");
                }

                var reader = new Utf8JsonReader(payload);
                return JsonSerializer.Deserialize<T>(ref reader, serializerOptions) ?? throw new InvalidOperationException("Deserialization of payload returned null");
            }

            /// <summary>
            ///     Converts the MQTT message payload to a FlatBuffer ByteBuffer for deserialization.
            /// </summary>
            /// <returns>A ByteBuffer wrapping the payload data.</returns>
            /// <remarks>
            ///     If the payload is a single segment and can be accessed as an array, a ByteBuffer is created with a reference to the underlying array.
            ///     Otherwise, the payload is copied to a new array.
            /// </remarks>
            public ByteBuffer GetFlatBufferPayload()
            {
                var payload = message.Payload;
                if (payload.IsSingleSegment && MemoryMarshal.TryGetArray(payload.First, out var segment))
                {
                    return new ByteBuffer(segment.Array, segment.Offset);
                }

                return new ByteBuffer(payload.ToArray());
            }

            /// <summary>
            ///     Tries to retrieve the correlation ID from the correlation data.
            /// </summary>
            /// <returns>
            ///     The extracted correlation ID as a <see cref="Guid" />, or <see cref="Guid.Empty" /> if the correlation data is null or in an unrecognized format.
            /// </returns>
            /// <remarks>
            ///     Supports 16-byte binary GUIDs and 36-character UTF-8 string GUIDs.
            /// </remarks>
            public Guid TryGetCorrelationId()
            {
                if (message.CorrelationData == null)
                {
                    return Guid.Empty;
                }

                return CorrelationDataParser.TryGetCorrelationId(message.CorrelationData);
            }

            /// <summary>
            ///     Retrieves the correlation ID from the correlation data />.
            /// </summary>
            /// <returns>The extracted correlation ID as a <see cref="Guid" />.</returns>
            /// <exception cref="MissingCorrelationIdException">Thrown if <see cref="MqttMessageReceived.CorrelationData" /> is <c>null</c>.</exception>
            /// <exception cref="InvalidCorrelationIdFormatException">Thrown if the correlation ID is not in a supported format (16-byte array or 36-character string).</exception>
            public Guid GetCorrelationId()
            {
                if (message.CorrelationData == null)
                {
                    throw new MissingCorrelationIdException(message.Topic);
                }

                var correlationId = CorrelationDataParser.TryGetCorrelationId(message.CorrelationData);
                if (correlationId == Guid.Empty)
                {
                    throw new InvalidCorrelationIdFormatException(message.Topic, message.CorrelationData);
                }

                return correlationId;
            }

            /// <summary>
            ///     Creates a message header dictionary from the MQTT response topic and correlation data.
            /// </summary>
            /// <returns>
            ///     A dictionary containing the response topic and correlation data (as Base64 string) if both are present,
            ///     otherwise null.
            /// </returns>
            /// <remarks>
            ///     This header can be passed to actor messages to enable request/response patterns.
            ///     Use <see cref="ActorContextExtensions.GetResponseTopic" /> and <see cref="ActorContextExtensions.GetCorrelationData" />
            ///     to retrieve these values from the actor context.
            /// </remarks>
            public Dictionary<string, string>? GetMessageHeader()
            {
                if (message.ResponseTopic == null || message.CorrelationData == null)
                {
                    return null;
                }

                return new Dictionary<string, string>
                       {
                           { MqttConstants.ResponseTopic, message.ResponseTopic },
                           { MqttConstants.CorrelationData, Convert.ToBase64String(message.CorrelationData) },
                       };
            }

            /// <summary>
            ///     Extracts the <see cref="ServiceProviderContractId" /> from an MQTT topic using the new topic structure:
            ///     <c>{installationTopic}/{serviceProviderId}/{service}/{contract}/{action...}</c>.
            /// </summary>
            /// <returns>A <see cref="ServiceProviderContractId" /> containing the extracted identifiers.</returns>
            /// <exception cref="InvalidOperationException">
            ///     Thrown when the topic does not contain at least three segments after the installation topic prefix.
            /// </exception>
            public ServiceProviderContractId ExtractServiceProviderContractId()
            {
                var topic = message.Topic.AsSpan();
                var dynamicStart = MqttConfiguration.InstallationTopic.Length + 1; // skip "{installationTopic}/"
                var remaining = topic[dynamicStart..];

                var firstSlash = remaining.IndexOf('/');
                var spId = remaining[..firstSlash].ToString();
                remaining = remaining[(firstSlash + 1)..];

                var secondSlash = remaining.IndexOf('/');
                var service = remaining[..secondSlash].ToString();
                remaining = remaining[(secondSlash + 1)..];

                var thirdSlash = remaining.IndexOf('/');
                var contract = (thirdSlash >= 0 ? remaining[..thirdSlash] : remaining).ToString();

                return new ServiceProviderContractId(spId, service, contract);
            }

            /// <summary>
            ///     Extracts a fixed number of slash-delimited segments from an MQTT topic that appear between two substrings of the topic.
            /// </summary>
            /// <param name="after">
            ///     The substring after which segment extraction begins. Pass <see cref="ReadOnlySpan{T}.Empty" /> to start extraction at the beginning of the topic.
            /// </param>
            /// <param name="before">
            ///     The substring before which segment extraction ends. Pass <see cref="ReadOnlySpan{T}.Empty" /> to extract until the end of the topic.
            /// </param>
            /// <param name="segmentCount">The exact number of segments expected between the two substrings.</param>
            /// <returns>An array containing the extracted segments.</returns>
            /// <exception cref="TopicSubstringNotFoundException">
            ///     Thrown when a non-empty <paramref name="after" /> or a non-empty <paramref name="before" /> cannot be located in the topic.
            /// </exception>
            /// <exception cref="UnexpectedSegmentCountException">
            ///     Thrown when the number of segments between the two substrings does not equal <paramref name="segmentCount" />.
            /// </exception>
            public string[] ExtractSegments(ReadOnlySpan<char> after, ReadOnlySpan<char> before, int segmentCount)
            {
                var topic = message.Topic.AsSpan();
                var sliceStart = 0;
                if (!after.IsEmpty)
                {
                    var startIndex = topic.IndexOf(after);
                    if (startIndex < 0)
                    {
                        throw new TopicSubstringNotFoundException(message.Topic, after.ToString());
                    }

                    sliceStart = startIndex + after.Length;
                }

                var sliceEnd = topic.Length;
                if (!before.IsEmpty)
                {
                    var relativeEndIndex = topic[sliceStart..].IndexOf(before); // Only search past the `after` match so `before` doesn't match something inside `after`
                    if (relativeEndIndex < 0)
                    {
                        throw new TopicSubstringNotFoundException(message.Topic, before.ToString());
                    }

                    sliceEnd = sliceStart + relativeEndIndex;
                }

                return message.SplitSegments(segmentCount, topic[sliceStart..sliceEnd].Trim('/'));
            }

            /// <summary>
            ///     Extracts a fixed number of slash-delimited segments from an MQTT topic that appear after a given substring and continue to the end of the topic.
            /// </summary>
            /// <param name="after">
            ///     The substring after which segment extraction begins. Pass <see cref="ReadOnlySpan{T}.Empty" /> to start extraction at the beginning of the topic.
            /// </param>
            /// <param name="segmentCount">The exact number of segments expected after the substring.</param>
            /// <returns>An array containing the extracted segments.</returns>
            /// <exception cref="TopicSubstringNotFoundException">
            ///     Thrown when a non-empty <paramref name="after" /> cannot be located in the topic.
            /// </exception>
            /// <exception cref="UnexpectedSegmentCountException">
            ///     Thrown when the number of segments after the substring does not equal <paramref name="segmentCount" />.
            /// </exception>
            public string[] ExtractSegments(ReadOnlySpan<char> after, int segmentCount)
            {
                return message.ExtractSegments(after, default, segmentCount);
            }

            private string[] SplitSegments(int segmentCount, ReadOnlySpan<char> segmentParts)
            {
                var segments = new string[segmentCount];
                var segmentIndex = 0;
                var segmentStart = 0;
                for (var i = 0; i <= segmentParts.Length; i++)
                {
                    if (i != segmentParts.Length && segmentParts[i] != '/')
                    {
                        continue;
                    }

                    if (segmentIndex == segments.Length)
                    {
                        var actualSegments = CountDynamicSegments(segmentParts) + 1; // +1 accounts for the first segment, which has no preceding slash due to Trim('/').
                        throw new UnexpectedSegmentCountException(message.Topic, segmentCount, actualSegments);
                    }

                    segments[segmentIndex++] = segmentParts.Slice(segmentStart, i - segmentStart).ToString();
                    segmentStart = i + 1;
                }

                if (segmentIndex != segmentCount)
                {
                    throw new UnexpectedSegmentCountException(message.Topic, segmentCount, segmentIndex);
                }

                return segments;

                static int CountDynamicSegments(ReadOnlySpan<char> dynamicSegmentParts)
                {
                    var count = 0;
                    for (var i = 0; i < dynamicSegmentParts.Length; i++)
                    {
                        if (dynamicSegmentParts[i] == '/')
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }

            private static string[] SplitSegments(ReadOnlySpan<char> segmentParts, int segmentCount, string topic)
            {
                var segments = new string[segmentCount];
                var segmentIndex = 0;
                var segmentStart = 0;
                for (var i = 0; i <= segmentParts.Length; i++)
                {
                    if (i != segmentParts.Length && segmentParts[i] != '/')
                    {
                        continue;
                    }

                    if (segmentIndex == segments.Length)
                    {
                        var actualSegments = CountDynamicSegments(segmentParts) + 1;
                        throw new UnexpectedSegmentCountException(topic, segmentCount, actualSegments);
                    }

                    segments[segmentIndex++] = segmentParts.Slice(segmentStart, i - segmentStart).ToString();
                    segmentStart = i + 1;
                }

                if (segmentIndex != segmentCount)
                {
                    throw new UnexpectedSegmentCountException(topic, segmentCount, segmentIndex);
                }

                return segments;

                static int CountDynamicSegments(ReadOnlySpan<char> dynamicSegmentParts)
                {
                    var count = 0;
                    for (var i = 0; i < dynamicSegmentParts.Length; i++)
                    {
                        if (dynamicSegmentParts[i] == '/')
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }
        }
    }

    /// <summary>
    ///     Exception thrown when an MQTT message is received without a correlation ID.
    /// </summary>
    public class MissingCorrelationIdException : Exception
    {
        public MissingCorrelationIdException(string topic) : base($"Received message without a correlation ID on topic '{topic}'.")
        {
        }
    }

    /// <summary>
    ///     Exception thrown when an MQTT message correlation ID is not in a supported format.
    /// </summary>
    /// <remarks>
    ///     Supported formats are: 16-byte array (binary GUID) or 36-character UTF-8 string (GUID string representation).
    /// </remarks>
    public class InvalidCorrelationIdFormatException : Exception
    {
        public InvalidCorrelationIdFormatException(string topic, byte[] correlationData) :
            base($"Received message with invalid correlation ID format on topic '{topic}': byte array length was {correlationData.Length}.")
        {
        }
    }

    public class UnexpectedSegmentCountException : Exception
    {
        public UnexpectedSegmentCountException(string topic, int expectedCount, int actualCount) :
            base($"Expected {expectedCount} segment(s) in topic '{topic}', but got {actualCount}.")
        {
        }
    }

    public class TopicSubstringNotFoundException : Exception
    {
        public TopicSubstringNotFoundException(string topic, string substring) : base($"Expected substring '{substring}' was not found in topic '{topic}'.")
        {
        }
    }
}