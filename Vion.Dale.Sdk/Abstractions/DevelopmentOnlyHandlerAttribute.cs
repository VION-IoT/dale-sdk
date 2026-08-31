using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Marks a <see cref="ServiceProviderHandlerBase" /> as development surface, so a production host can
    ///     leave it out of handler registration by reading the type alone.
    ///     <para>
    ///         Declare it on the handler of a provider face — the contract a simulator binds to stand in for
    ///         equipment that is not there. Such a handler has no hardware behind it: it subscribes to no MQTT
    ///         topic and moves no message, and a host that stands it up anyway registers an empty routing key,
    ///         which is a live hazard wherever handlers are matched by prefix or substring. The development
    ///         host discovers provider handlers by their <see cref="ScenarioWireAttribute" /> as before; this
    ///         marker is what a production host filters on.
    ///     </para>
    ///     <para>
    ///         It is a declaration about the handler and carries no runtime behaviour. It deliberately does not
    ///         depend on the contract type the handler services — no such link exists at the type level — so
    ///         declare it beside the contract type's own
    ///         <c>[ServiceProviderContractType(…, DevelopmentOnly = true)]</c>, and keep the two in step.
    ///     </para>
    ///     <code>
    /// [DevelopmentOnlyHandler]
    /// [ScenarioWire(Inbound = typeof(SetDigitalOutput), Outbound = typeof(DigitalOutputChanged))]
    /// public class DigitalOutputProviderHandler : ServiceProviderHandlerBase
    ///     </code>
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DevelopmentOnlyHandlerAttribute : Attribute
    {
    }
}