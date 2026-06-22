using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.DevHost.SmokeHost.Contracts
{
    /// <summary>The grid-demand scope (proves an enum field round-trips through the wire JSON, by name).</summary>
    public enum DemandScope
    {
        Total,

        PerPhase,
    }

    /// <summary>A 1-level NESTED struct inside the wire payload — the case the HAL contracts never exercise.</summary>
    public readonly record struct PowerLimits(double ActivePowerW, double ReactivePowerVar);

    /// <summary>The wire struct a grid demand carries: multi-field, an enum, and a nested struct (PPC-shaped).</summary>
    public readonly record struct GridDemandReceived(bool Valid, DemandScope Scope, PowerLimits Limits);

    /// <summary>
    ///     A synthetic third-party-shaped service-provider <b>value</b> contract for the SmokeHost — nothing
    ///     HAL-specific. Its wire struct is multi-field with a 1-level nested struct + an enum, so it proves the
    ///     RFC 0010 generic path drives a struct payload end to end (<c>serviceProviderSet</c>) and that the
    ///     wiring panel renders a non-HAL contract honestly ("SP" / scenario-driven).
    /// </summary>
    [ServiceProviderContractType("GridDemand")]
    public interface IGridDemand
    {
        event EventHandler<GridDemandReceived>? DemandReceived;
    }

    /// <summary>The consumer-side contract: dispatches the inbound wire struct to <see cref="DemandReceived" />.</summary>
    public class GridDemand : LogicBlockContractBase, IGridDemand
    {
        public override string ContractHandlerActorName { get; protected set; } = nameof(GridDemandHandler);

        public GridDemand(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public event EventHandler<GridDemandReceived>? DemandReceived;

        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            if (contractMessage is ContractMessage<GridDemandReceived> m)
            {
                DemandReceived?.Invoke(this, m.Data);
            }
        }
    }

    /// <summary>
    ///     The provider handler — discovered by the convention scan for its class name + <c>[ScenarioWire]</c>.
    ///     In the DevHost the generic stand-in is created under this name; the real (MQTT) handler is never
    ///     instantiated here, so the MQTT members are inert.
    /// </summary>
    [ScenarioWire(Inbound = typeof(GridDemandReceived))]
    public class GridDemandHandler : ServiceProviderHandlerBase
    {
        public GridDemandHandler(ILogger<GridDemandHandler> logger) : base(logger)
        {
        }

        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return ("grid", new[] { "/demand" });
        }

        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
        }

        protected override void HandleContractMessage(IContractMessage message)
        {
        }
    }
}