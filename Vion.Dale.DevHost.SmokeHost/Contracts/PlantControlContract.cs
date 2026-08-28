using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;

namespace Vion.Dale.DevHost.SmokeHost.Contracts
{
    /// <summary>The setpoints a plant demand carries — a 1-level NESTED struct inside the inbound payload.</summary>
    public readonly record struct PlantSupply(double ActivePowerKw, double ReactivePowerKvar);

    /// <summary>The wire struct the SP-issued demand carries (the INBOUND half): multi-field, an enum, a nested struct.</summary>
    public readonly record struct PlantDemandReceived(bool Valid, DemandScope Scope, PlantSupply Supply);

    /// <summary>
    ///     The command the block publishes back (the OUTBOUND half of the SAME contract): multi-field, with a
    ///     publish-time stamp. <see cref="Timestamp" /> is stamped by the contract as it writes, from the wall
    ///     clock rather than the virtual one, so it is <b>deliberately non-deterministic</b> — a scenario asserts
    ///     the fields around it and leaves it alone.
    /// </summary>
    public readonly record struct PlantMeasurementSet(bool Valid, DateTimeOffset Timestamp, double ActivePowerKw, double ReactivePowerKvar);

    /// <summary>
    ///     A synthetic third-party-shaped service-provider value contract that is <b>bidirectional</b>: one
    ///     interface, one contract identifier, carrying an inbound demand <i>and</i> an outbound measurement.
    ///     It declares no <c>Consumers</c>, so it carries the default <see cref="LinkMultiplicity.ZeroOrMore" />
    ///     and classifies as an <b>input</b> — while its handler's <c>[ScenarioWire]</c> declares both wire
    ///     directions. That combination is the shape <see cref="IGridDemand" /> + <see cref="IGridSetpoint" />
    ///     never had, because they split the two directions across two contracts: a scenario must be able to
    ///     drive this one with <c>serviceProviderSet</c> <b>and</b> assert it with <c>serviceProviderExpect</c>
    ///     (VION-129). It mirrors the real <c>IPowerPlantControlPv</c> in <c>logic-block-libraries</c>.
    /// </summary>
    [ServiceProviderContractType("PlantControl")]
    public interface IPlantControl
    {
        /// <summary>Occurs when the service provider issues a fresh demand (SP → block).</summary>
        event EventHandler<PlantDemandReceived>? DemandReceived;

        /// <summary>Publish the block's aggregated measurement back to the service provider (block → SP).</summary>
        void SetMeasurement(bool valid, double activePowerKw, double reactivePowerKvar);
    }

    /// <summary>
    ///     The consumer-side contract: dispatches the inbound wire struct to <see cref="DemandReceived" /> and
    ///     builds the outbound wire struct for <see cref="SetMeasurement" />.
    /// </summary>
    public class PlantControl : LogicBlockContractBase, IPlantControl
    {
        public override string ContractHandlerActorName { get; protected set; } = nameof(PlantControlHandler);

        public PlantControl(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public event EventHandler<PlantDemandReceived>? DemandReceived;

        public void SetMeasurement(bool valid, double activePowerKw, double reactivePowerKvar)
        {
            SendToContractHandler(new ContractMessage<PlantMeasurementSet>(LogicBlockContractId,
                                                                           new PlantMeasurementSet(valid, DateTimeOffset.UtcNow, activePowerKw, reactivePowerKvar)));
        }

        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            if (contractMessage is ContractMessage<PlantDemandReceived> m)
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
    [ScenarioWire(Inbound = typeof(PlantDemandReceived), Outbound = typeof(PlantMeasurementSet))]
    public class PlantControlHandler : ServiceProviderHandlerBase
    {
        public PlantControlHandler(ILogger<PlantControlHandler> logger) : base(logger)
        {
        }

        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return ("plant", new[] { "/demand", "/measurement" });
        }

        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
        }

        protected override void HandleContractMessage(IContractMessage message)
        {
        }
    }
}