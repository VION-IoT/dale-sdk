using System.Collections.Generic;

namespace Vion.Dale.DevHost.Mocking
{
    /// <summary>
    ///     The names the generic service-provider stand-ins were registered under — one per discovered
    ///     <c>[ScenarioWire]</c> handler, recorded by <c>DevLogicSystemInitializer</c> as it creates them, so the
    ///     rest of the host addresses "every stand-in" without naming a single contract.
    ///     <para>
    ///         Two callers need exactly that set: the contract link map is fanned out to all of them (RFC 0010),
    ///         and <c>DevHostControl.PublishAllStates</c> replays each one's last inbound/outbound to a late web
    ///         subscriber. The replay used to name the four HAL handlers literally, which left every other value
    ///         contract — a consumer's own, a provider face — dark in a browser that connected after the value was
    ///         written (RFC 0010's "no hardcoded contract support", RFC 0020 §7).
    ///     </para>
    /// </summary>
    internal sealed class ServiceProviderStandIns
    {
        private readonly List<string> _names = [];

        /// <summary>The handler class names stand-ins were created under, in discovery order.</summary>
        public IReadOnlyList<string> Names
        {
            get => _names;
        }

        public void Add(string handlerActorName)
        {
            _names.Add(handlerActorName);
        }
    }
}