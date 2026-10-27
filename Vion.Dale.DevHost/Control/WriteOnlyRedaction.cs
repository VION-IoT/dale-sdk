using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json.Nodes;
using Vion.Contracts.Codec;
using Vion.Contracts.TypeRef;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     The DevHost's half of the write-only posture. A service property's write-only positions are redacted to
    ///     the sentinel on everything that leaves the host's process — the state routes, the captured-message
    ///     route, the push wire, the host's own log lines — and an echoed sentinel is resolved on a JSON write, both
    ///     through the platform's <see cref="WriteOnlyCodec" /> keyed off the property's schema, as the gateway's
    ///     runtime does. In process the value stays as the block published it. One per host generation.
    /// </summary>
    public sealed class WriteOnlyRedaction
    {
        private readonly DevConfiguration _configuration;

        private readonly DevHostIntrospection _introspection;

        // (service-config id, property) → the property's schema when it has a write-only position, else null; a
        // property without one passes through untouched, so nothing else on the wire changes shape.
        private readonly ConcurrentDictionary<(string ServiceId, string Property), TypeSchema?> _schemas = new();

        public WriteOnlyRedaction(DevConfiguration configuration, DevHostIntrospection introspection)
        {
            _configuration = configuration;
            _introspection = introspection;
        }

        /// <summary>
        ///     The value of a property addressed by service-config id with each write-only position replaced by the
        ///     sentinel and a null one left null, as JSON; any other member's value as it is.
        /// </summary>
        public object? Redact(string serviceId, string propertyName, object? value)
        {
            return WriteOnlySchema(serviceId, propertyName) is { } schema ? WriteOnlyCodec.Redact(schema, PropertyValueCodec.ClrToJson(value, schema.Type)) : value;
        }

        /// <summary>
        ///     <see cref="Redact" /> for a member addressed the way a state read addresses it: a block by name or id,
        ///     the member by bare name or dotted <c>service.member</c> path.
        /// </summary>
        public object? RedactMember(string logicBlockIdOrName, string memberPath, object? value)
        {
            var logicBlock = _configuration.LogicBlocks.FirstOrDefault(b => b.Name == logicBlockIdOrName) ??
                             _configuration.LogicBlocks.FirstOrDefault(b => b.Id == logicBlockIdOrName);
            return logicBlock is not null && _introspection.TryResolveMember(logicBlock.Id, memberPath, out var serviceId, out var member)
                       ? Redact(serviceId, member, value)
                       : value;
        }

        /// <summary>A captured message with a service property's value redacted; any other message as it is.</summary>
        public object RedactMessage(object message)
        {
            return message switch
            {
                SetServicePropertyValueRequest m => m with { Value = Redact(m.ServiceIdentifier.ToString(), m.PropertyIdentifier, m.Value) },
                SetServicePropertyValueResponse m => m with { Value = Redact(m.ServiceIdentifier.ToString(), m.PropertyIdentifier, m.Value) },
                ServicePropertyValueChanged m => m with { Value = Redact(m.ServiceIdentifier.ToString(), m.PropertyIdentifier, m.Value) },
                MockSetServicePropertyValue m => m with { Request = (SetServicePropertyValueRequest)RedactMessage(m.Request) },
                _ => message,
            };
        }

        // An echoed sentinel at a write-only position becomes the value the cache holds for the property; a null
        // position stays null, an explicit clear. Any other property's write passes through.
        internal JsonNode? Resolve(string serviceId, string propertyName, JsonNode? incoming, object? current)
        {
            return WriteOnlySchema(serviceId, propertyName) is { } schema ? WriteOnlyCodec.Resolve(schema, incoming, PropertyValueCodec.ClrToJson(current, schema.Type)) : incoming;
        }

        private TypeSchema? WriteOnlySchema(string serviceId, string propertyName)
        {
            return _schemas.GetOrAdd((serviceId, propertyName),
                                     key =>
                                     {
                                         _introspection.TryGetPropertyConversion(key.ServiceId, key.Property, out var json, out _);
                                         if (json is null)
                                         {
                                             return null;
                                         }

                                         var schema = TypeSchemaSerialization.FromJsonSchema(json);
                                         return schema.Annotations.WriteOnly || schema.StructFieldAnnotations.Values.Any(annotations => annotations.WriteOnly) ? schema : null;
                                     });
        }
    }
}
