using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.Utils;

namespace Vion.Examples.Energy.LogicBlocks
{
    [LogicBlockInfo("Ladestation Simulation", "charging-pile-2-line")]
    public class ChargingStationSimulation : LogicBlockBase, IControllableElectricityConsumer
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly ILogger _logger;

        private bool _enableCharging;

        private DateTime? _lastUpdateTime;

        private double _maximumActivePower = 10;

        private double _requestedActivePower;

        [ServiceProviderContract(defaultName: "Externe Sperre")]
        public IDigitalInput ExternallyLockedInput { get; private set; }

        [ServiceProviderContract(defaultName: "Ladevorgang aktiv")]
        public IDigitalOutput ChargingOutput { get; private set; }

        [ServiceProperty(Title = "Externe Sperre aktiv")]
        [Display(group: "Status")]
        public bool IsExternallyLocked { get; private set; }

        [ServiceProperty(Title = "Maximale Wirkleistung", Unit = "kW")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public double MaximumActivePower
        {
            get => _maximumActivePower;

            set
            {
                if (_maximumActivePower != value) // on change
                {
                    _maximumActivePower = value;
                    UpdateRequestedPower();
                }
            }
        }

        [ServiceProperty(Title = "Ladefreigabe")]
        [Category(PropertyCategory.Configuration)]
        [Importance(Importance.Secondary)]
        [Display(group: "Konfiguration")]
        public bool EnableCharging
        {
            get => _enableCharging;

            set
            {
                if (_enableCharging != value) // on change
                {
                    _enableCharging = value;
                    UpdateRequestedPower();
                }
            }
        }

        [ServiceProperty(Title = "Wirkleistung", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung", Unit = "kW")]
        [Importance(Importance.Primary)]
        [Display(group: "Status")]
        public double ActivePowerConsuming { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Gesamtverbrauch Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Gesamtverbrauch Total", Unit = "kWh")]
        [Category(PropertyCategory.Metric)]
        [Display(group: "Zähler")]
        public double EnergyConsumedTotal { get; private set; }

        [ServiceProperty(Title = "Angeforderte Wirkleistung", Unit = "kW")]
        [Display(group: "Status")]
        public double RequestedActivePower
        {
            get => _requestedActivePower;

            private set
            {
                if (_requestedActivePower != value) // on change
                {
                    _requestedActivePower = value;
                    this.SendStateUpdate(new ControllableElectricityConsumerContract.StateUpdate(value));
                }
            }
        }

        [ServiceProperty(Title = "Zugewiesene Wirkleistung", Unit = "kW")]
        [Display(group: "Status")]
        public double AllocatedActivePower { get; private set; }

        public ChargingStationSimulation(IDateTimeProvider dateTimeProvider, ILogger logger) : base(logger)
        {
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public ControllableElectricityConsumerContract.DataResponse HandleRequest(ControllableElectricityConsumerContract.DataRequest request)
        {
            return new ControllableElectricityConsumerContract.DataResponse(_dateTimeProvider.UtcNow, ActivePowerConsuming, EnergyConsumedTotal);
        }

        /// <inheritdoc />
        public void HandleCommand(ControllableElectricityConsumerContract.Command command)
        {
            AllocatedActivePower = command.AllocatedActivePower;
            _logger.LogInformation("Received command with allocated power: {AllocatedPower:F3} kW", command.AllocatedActivePower);
        }

        [Timer(5)]
        public void OnTimer()
        {
            var currentTime = _dateTimeProvider.UtcNow;
            if (_lastUpdateTime.HasValue)
            {
                var newActivePower = Math.Min(AllocatedActivePower, RequestedActivePower);

                var energyIncrement = EnergyCalculator.CalculateEnergyIncrement(ActivePowerConsuming, newActivePower, _lastUpdateTime.Value, currentTime);

                EnergyConsumedTotal += energyIncrement;

                _logger.LogDebug("Energy increment: {EnergyInc:F6} kWh", energyIncrement);

                ActivePowerConsuming = newActivePower;
                ChargingOutput.Set(ActivePowerConsuming > 0);

                _logger.LogInformation("Active power: {Power:F3} kW, Total energy: {Energy:F3} kWh", ActivePowerConsuming, EnergyConsumedTotal);
            }

            _lastUpdateTime = currentTime;

            this.SendStateUpdate(new ControllableElectricityConsumerContract.GridEffectStateUpdate(ActivePowerConsuming));
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            ExternallyLockedInput.InputChanged += (_, value) =>
                                                  {
                                                      _logger.LogInformation("Externally locked state changed: {ExternallyLockedInput}", value);
                                                      IsExternallyLocked = value;
                                                      UpdateRequestedPower();
                                                  };
        }

        /// <inheritdoc />
        protected override void Starting()
        {
            this.SendStateUpdate(new ControllableElectricityConsumerContract.StateUpdate(RequestedActivePower));
        }

        private void UpdateRequestedPower()
        {
            RequestedActivePower = EnableCharging && !IsExternallyLocked ? MaximumActivePower : 0;
        }
    }
}