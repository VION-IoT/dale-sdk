using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Scenarios
{
    /// <summary>
    ///     The DevHost-side codec behind a contract's <see cref="ScenarioWireAttribute" /> (RFC 0010): builds the
    ///     exact closed <c>ContractMessage&lt;TInbound&gt;</c> a consumer's <c>HandleContractMessage</c> switch
    ///     matches from a scenario JSON value (drive), and decodes an output command back to a JSON value (assert).
    ///     Reflects over the declared wire <see cref="Type" /> — the DevHost never references a consumer's wire
    ///     structs. Test-only: it produces the SAME CLR wire payload the production handler forwards, just sourced
    ///     from a scenario's JSON value instead of a FlatBuffer MQTT frame. A single-field wire struct round-trips
    ///     as its scalar field (so a digital input is driven by <c>true</c>); a multi-field struct as a JSON object.
    /// </summary>
    internal sealed class ScenarioWireCodec
    {
        private readonly Type? _inbound;

        private readonly Type? _outbound;

        /// <summary>True when a scenario can DRIVE this contract (an input).</summary>
        public bool CanDrive
        {
            get => _inbound is not null;
        }

        /// <summary>True when a scenario can ASSERT this contract's last written command (an output).</summary>
        public bool CanAssert
        {
            get => _outbound is not null;
        }

        private ScenarioWireCodec(Type? inbound, Type? outbound)
        {
            _inbound = inbound;
            _outbound = outbound;
        }

        /// <summary>
        ///     Build a codec from a service-provider handler type's <see cref="ScenarioWireAttribute" />, or null when
        ///     undeclared.
        /// </summary>
        public static ScenarioWireCodec? ForHandler(Type handlerType)
        {
            var attribute = handlerType.GetCustomAttribute<ScenarioWireAttribute>();
            if (attribute is null || (attribute.Inbound is null && attribute.Outbound is null))
            {
                return null;
            }

            return new ScenarioWireCodec(attribute.Inbound, attribute.Outbound);
        }

        /// <summary>Drive: a scenario value → the exact closed <c>ContractMessage&lt;TInbound&gt;</c>.</summary>
        public IContractMessage MakeInbound(LogicBlockContractId contractId, JsonElement value)
        {
            if (_inbound is null)
            {
                throw new InvalidOperationException("This contract is an output — assert it with serviceProviderExpect; it cannot be driven.");
            }

            var data = Decode(_inbound, value);
            var messageType = typeof(ContractMessage<>).MakeGenericType(_inbound);
            return (IContractMessage)Activator.CreateInstance(messageType, contractId, data)!;
        }

        /// <summary>Assert: decode the command a block wrote (a <c>ContractMessage&lt;TOutbound&gt;</c>) back to a JSON value.</summary>
        public JsonElement ReadCommand(IContractMessage commandFromBlock)
        {
            if (_outbound is null)
            {
                throw new InvalidOperationException("This contract is an input — drive it with serviceProviderSet; it has nothing to assert.");
            }

            var data = commandFromBlock.GetType().GetProperty("Data")!.GetValue(commandFromBlock)!;
            return Encode(data);
        }

        // JSON value → wire struct. A single-parameter struct (e.g. DigitalInputChanged(bool)) binds a scalar to
        // its one constructor parameter so the scenario value stays a scalar; a multi-parameter struct deserializes
        // as a JSON object. Enums by name (JsonSerialization.DefaultOptions).
        private static object Decode(Type structType, JsonElement value)
        {
            var constructor = structType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = constructor.GetParameters();
            if (parameters.Length == 1 && value.ValueKind != JsonValueKind.Object)
            {
                var argument = value.Deserialize(parameters[0].ParameterType, JsonSerialization.DefaultOptions);
                return constructor.Invoke(new[] { argument });
            }

            return value.Deserialize(structType, JsonSerialization.DefaultOptions)!;
        }

        // Wire struct → JSON value, symmetric with Decode: a single-field struct unwraps to its scalar.
        private static JsonElement Encode(object data)
        {
            var structType = data.GetType();
            var constructor = structType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = constructor.GetParameters();
            if (parameters.Length == 1)
            {
                var field = structType.GetProperty(parameters[0].Name!);
                if (field is not null)
                {
                    return JsonSerializer.SerializeToElement(field.GetValue(data), JsonSerialization.DefaultOptions);
                }
            }

            return JsonSerializer.SerializeToElement(data, JsonSerialization.DefaultOptions);
        }
    }
}