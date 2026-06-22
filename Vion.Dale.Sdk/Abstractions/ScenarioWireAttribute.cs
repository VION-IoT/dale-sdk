using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Marks a <see cref="ServiceProviderHandlerBase" /> with the wire struct its contract carries, so the
    ///     DevHost can drive (<c>serviceProviderSet</c>) and assert (<c>serviceProviderExpect</c>) that contract
    ///     from a committed scenario through the generic service-provider handler (RFC 0010).
    ///     <para>
    ///         <b>Scenario-testing / DevHost only.</b> The production runtime reaches hardware over MQTT
    ///         (FlatBuffers) and never reads this — it carries no runtime behaviour. It is a declarative marker the
    ///         DevHost discovers (the same assembly scan the runtime uses to find handlers) to build the contract
    ///         message from a JSON scenario value.
    ///     </para>
    ///     <para>
    ///         Declare the inbound struct for an input contract (SP → block, driven by a scenario), and/or the
    ///         outbound command struct for an output contract (block → SP, asserted by a scenario):
    ///         <code>
    /// [ScenarioWire(Inbound = typeof(DigitalInputChanged))]   // an input — digital/analog input, PPC demand
    /// [ScenarioWire(Outbound = typeof(SetDigitalOutput))]     // an output — digital/analog output
    ///         </code>
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