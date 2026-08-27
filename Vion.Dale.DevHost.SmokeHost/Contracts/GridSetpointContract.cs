using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;

namespace Vion.Dale.DevHost.SmokeHost.Contracts
{
    /// <summary>The limits a setpoint command carries — a 1-level NESTED struct inside the outbound payload.</summary>
    public readonly record struct SetpointLimits(double ActivePowerW, double ReactivePowerVar);

    /// <summary>
    ///     The command struct a grid setpoint carries: multi-field, with an enum, a nested struct, and a
    ///     publish-time stamp. <see cref="IssuedAt" /> is stamped by the contract as it writes, from the wall
    ///     clock rather than the virtual one, so it is <b>deliberately non-deterministic</b> — a scenario asserts
    ///     the fields around it and leaves it alone. That is the shape this fixture exists to cover.
    ///     <see cref="Limits" /> is absent while nothing is enforced, so a field path through it is addressable
    ///     but not always readable — the case a scenario must be told about rather than silently pass.
    /// </summary>
    public readonly record struct SetGridSetpoint(bool Enforced, DemandScope Scope, SetpointLimits? Limits, DateTimeOffset IssuedAt);

    /// <summary>
    ///     A synthetic third-party-shaped service-provider value <b>output</b> contract for the SmokeHost — the
    ///     outbound counterpart of <see cref="IGridDemand" />. Its command is a multi-field struct, so it does
    ///     not round-trip as a bare scalar the way the four HAL outputs do: a scenario asserts it one field at a
    ///     time with <c>serviceProviderExpect</c>'s <c>field</c>.
    /// </summary>
    [ServiceProviderContractType("GridSetpoint", Consumers = LinkMultiplicity.ZeroOrOne)]
    public interface IGridSetpoint
    {
        /// <summary>Write the setpoint command, stamped with the time it was issued.</summary>
        void Set(bool enforced, DemandScope scope, SetpointLimits? limits);
    }

    /// <summary>The consumer-side contract: builds the outbound wire struct and writes it to the handler.</summary>
    public class GridSetpoint : LogicBlockContractBase, IGridSetpoint
    {
        public override string ContractHandlerActorName { get; protected set; } = nameof(GridSetpointHandler);

        public GridSetpoint(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public void Set(bool enforced, DemandScope scope, SetpointLimits? limits)
        {
            SendToContractHandler(new ContractMessage<SetGridSetpoint>(LogicBlockContractId, new SetGridSetpoint(enforced, scope, limits, DateTimeOffset.UtcNow)));
        }

        public override void HandleContractMessage(IContractMessage contractMessage)
        {
        }
    }

    /// <summary>
    ///     The provider handler — discovered by the convention scan for its class name + <c>[ScenarioWire]</c>.
    ///     In the DevHost the generic stand-in is created under this name and captures the command; the real
    ///     (MQTT) handler is never instantiated here, so the MQTT members are inert.
    /// </summary>
    [ScenarioWire(Outbound = typeof(SetGridSetpoint))]
    public class GridSetpointHandler : ServiceProviderHandlerBase
    {
        public GridSetpointHandler(ILogger<GridSetpointHandler> logger) : base(logger)
        {
        }

        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return ("grid", new[] { "/setpoint" });
        }

        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
        }

        protected override void HandleContractMessage(IContractMessage message)
        {
        }
    }
}