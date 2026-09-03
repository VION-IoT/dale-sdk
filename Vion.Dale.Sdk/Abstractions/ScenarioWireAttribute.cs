using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Marks a <see cref="ServiceProviderHandlerBase" /> with the wire struct its contract carries, so the
    ///     DevHost can drive (<c>serviceProviderSet</c>) and assert (<c>serviceProviderExpect</c>) that contract
    ///     from a committed scenario through the generic service-provider handler.
    ///     <para>
    ///         <b>Scenario-testing / DevHost only.</b> The production runtime reaches hardware over MQTT
    ///         (FlatBuffers) and never reads this — it carries no runtime behaviour. It is a declarative marker the
    ///         DevHost discovers (the same assembly scan the runtime uses to find handlers) to build the contract
    ///         message from a JSON scenario value.
    ///     </para>
    ///     <para>
    ///         Declare the inbound struct the service provider delivers (SP → block, driven by a scenario),
    ///         and/or the outbound command struct the block writes (block → SP, asserted by a scenario).
    ///         Declaring BOTH makes one contract identifier drivable and assertable in the same scenario —
    ///         a bidirectional contract, or an output whose provider confirms back what it applied. A handler
    ///         carries exactly ONE of these; the attribute is not stackable.
    ///     </para>
    ///     <para>An input — a digital/analog input, a PPC demand:</para>
    ///     <code>
    /// [ScenarioWire(Inbound = typeof(DigitalInputChanged))]
    ///     </code>
    ///     <para>An output that is confirmed back:</para>
    ///     <code>
    /// [ScenarioWire(Inbound = typeof(DigitalOutputChanged), Outbound = typeof(SetDigitalOutput))]
    ///     </code>
    ///     <para>Bidirectional — one contract identifier, both directions:</para>
    ///     <code>
    /// [ScenarioWire(Inbound = typeof(PpcDemandReceived), Outbound = typeof(PpcMeasurementSet))]
    ///     </code>
    ///     <para>
    ///         A contract with no declared inbound cannot be driven; one with no declared outbound has nothing
    ///         to assert.
    ///     </para>
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ScenarioWireAttribute : Attribute
    {
        /// <summary>The wire struct a scenario DRIVES into the block (an input contract); a scenario value maps to it.</summary>
        public Type? Inbound { get; init; }

        /// <summary>The command struct the block writes and a scenario ASSERTS (an output contract).</summary>
        public Type? Outbound { get; init; }
    }
}