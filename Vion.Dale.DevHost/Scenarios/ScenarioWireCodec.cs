using System;
using System.Collections.Generic;
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

        /// <summary>
        ///     The scalar field leaves a <c>serviceProviderExpect</c> <c>field</c> may address on this
        ///     contract's outbound command, or null when the contract has no outbound (an input). EMPTY when
        ///     the outbound round-trips as a bare scalar (the single-field unwrap below) — such an output is
        ///     asserted directly, with no field.
        /// </summary>
        public IReadOnlyList<string>? OutputFieldPaths
        {
            get
            {
                if (_outbound is null)
                {
                    return null;
                }

                return UnwrappedField(_outbound) is not null ? Array.Empty<string>() : ScenarioWireFields.LeafPaths(_outbound);
            }
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
            var field = UnwrappedField(structType);

            return field is not null ? JsonSerializer.SerializeToElement(field.GetValue(data), JsonSerialization.DefaultOptions) :
                       JsonSerializer.SerializeToElement(data, JsonSerialization.DefaultOptions);
        }

        // The one property a single-field wire struct unwraps to on the wire, or null when the struct
        // serializes as a JSON object. The single owner of the unwrap rule: Encode writes through it, and
        // OutputFieldPaths reports "no addressable field" for exactly the shapes it accepts. A struct that
        // declares no constructor at all (init-only properties) serializes as an object like any other — it must
        // not throw, because OutputFieldPaths runs over every discovered handler when the configuration is built,
        // not only when a block writes a command.
        private static PropertyInfo? UnwrappedField(Type structType)
        {
            var constructor = structType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            var parameters = constructor?.GetParameters();

            return parameters is { Length: 1 } ? structType.GetProperty(parameters[0].Name!) : null;
        }
    }
}