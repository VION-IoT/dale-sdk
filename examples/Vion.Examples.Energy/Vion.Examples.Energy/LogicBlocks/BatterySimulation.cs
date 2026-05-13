using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using System;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.Utils;

namespace Vion.Examples.Energy.LogicBlocks
{
    [LogicBlock(Name = "Batterie Simulation", Icon = "battery-2-charge-line")]
    public class BatterySimulation : LogicBlockBase, IControllableElectricityBuffer
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly ILogger _logger;

        private DateTime? _lastUpdateTime;

        [ServiceProviderContractBinding(DefaultName = "Ladezustand")]
        public IAnalogOutput StateOfChargeOutput { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Batterie lädt")]
        public IDigitalOutput BatteryChargingOutput { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Batterie entlädt")]
        public IDigitalOutput BatteryDischargingOutput { get; private set; }

        [ServiceProperty(Title = "Ladezustand", Unit = "%")]
        [ServiceMeasuringPoint]
        [Persistent]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public double StateOfCharge { get; private set; }

        [ServiceProperty(Title = "Kapazität", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public double Capacity { get; set; } = 100;

        [ServiceProperty(Title = "Maximale Wirkleistung Laden", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public double MaximumActivePowerCharging { get; set; } = 10;

        [ServiceProperty(Title = "Maximale Wirkleistung Entladen", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public double MaximumActivePowerDischarging { get; set; } = 10;

        [ServiceProperty(Title = "Wirkleistung Laden", Unit = "kW")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public double ActivePowerCharging { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Entladen", Unit = "kW")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public double ActivePowerDischarging { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Laden Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Kind = MeasuringPointKind.TotalIncreasing)]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyChargedTotal { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Entladen Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Kind = MeasuringPointKind.TotalIncreasing)]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyDischargedTotal { get; private set; }

        [ServiceProperty(Title = "Aktuelle maximale Wirkleistung Entladen", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double CurrentMaximumActivePowerDischarging { get; private set; }

        [ServiceProperty(Title = "Aktuelle maximale Wirkleistung Laden", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double CurrentMaximumActivePowerCharging { get; private set; }

        public BatterySimulation(IDateTimeProvider dateTimeProvider, ILogger logger) : base(logger)
        {
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public ControllableElectricityBufferContract.DataResponse HandleRequest(ControllableElectricityBufferContract.DataRequest request)
        {
            return new ControllableElectricityBufferContract.DataResponse(_dateTimeProvider.UtcNow,
                                                                          ActivePowerCharging,
                                                                          ActivePowerDischarging,
                                                                          EnergyChargedTotal,
                                                                          EnergyDischargedTotal,
                                                                          StateOfCharge);
        }

        /// <inheritdoc />
        public void HandleCommand(ControllableElectricityBufferContract.Command command)
        {
            ActivePowerCharging = Math.Clamp(command.AllocatedActivePowerCharging, 0, CurrentMaximumActivePowerCharging);
            ActivePowerDischarging = Math.Clamp(command.AllocatedActivePowerDischarging, 0, CurrentMaximumActivePowerDischarging);
        }

        [Timer(5)]
        public void OnTimer()
        {
            var currentTime = _dateTimeProvider.UtcNow;
            if (_lastUpdateTime.HasValue)
            {
                var energyIncrementCharging = EnergyCalculator.CalculateEnergyIncrement(ActivePowerCharging, ActivePowerCharging, _lastUpdateTime.Value, currentTime);

                EnergyChargedTotal += energyIncrementCharging;
                _logger.LogDebug("EnergyChargedTotal increment: {EnergyInc:F6} kWh", energyIncrementCharging);

                var energyIncrementDischarging = EnergyCalculator.CalculateEnergyIncrement(ActivePowerDischarging, ActivePowerDischarging, _lastUpdateTime.Value, currentTime);

                EnergyDischargedTotal += energyIncrementDischarging;
                _logger.LogDebug("EnergyDischargedTotal increment: {EnergyInc:F6} kWh", energyIncrementDischarging);

                var netChargingIncrementSigned = energyIncrementCharging - energyIncrementDischarging;
                var stateOfPercentChargeDiff = netChargingIncrementSigned / Capacity * 100;
                StateOfCharge = Math.Clamp(StateOfCharge + stateOfPercentChargeDiff, 0.0, 100.0);

                CurrentMaximumActivePowerDischarging = StateOfCharge == 0.0 ? 0.0 : MaximumActivePowerDischarging;
                CurrentMaximumActivePowerCharging = StateOfCharge == 100.0 ? 0.0 : MaximumActivePowerCharging;

                StateOfChargeOutput.Set(StateOfCharge);
                BatteryChargingOutput.Set(ActivePowerCharging > 0);
                BatteryDischargingOutput.Set(ActivePowerDischarging > 0);
            }

            _lastUpdateTime = currentTime;

            // todo: maybe send only if changed
            this.SendStateUpdate(new ControllableElectricityBufferContract.StateUpdate(CurrentMaximumActivePowerCharging, CurrentMaximumActivePowerDischarging, Capacity));

            var activePowerChargingSigned = ActivePowerCharging - ActivePowerDischarging;
            this.SendStateUpdate(new ControllableElectricityBufferContract.GridEffectStateUpdate(activePowerChargingSigned));
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
}