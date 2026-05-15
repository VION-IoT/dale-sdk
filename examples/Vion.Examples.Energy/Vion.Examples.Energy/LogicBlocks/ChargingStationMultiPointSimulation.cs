using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using System;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.Utils;

namespace Vion.Examples.Energy.LogicBlocks
{
    [LogicBlock(Name = "Ladestation Multi-Point Simulation", Icon = "charging-pile-2-line")]
    public class ChargingStationMultiPointSimulation : LogicBlockBase
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly ILogger _logger;

        [ServiceProviderContractBinding(DefaultName = "Externe Sperre")]
        public IDigitalInput ExternallyLockedInput { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Ladepunkt 1 aktiv")]
        public IDigitalOutput ChargingPoint1Output { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Ladepunkt 2 aktiv")]
        public IDigitalOutput ChargingPoint2Output { get; private set; }

        [ServiceProperty(Title = "Externe Sperre aktiv")]
        [Presentation(Group = PropertyGroup.Status)]
        public bool IsExternallyLocked { get; private set; }

        public ChargingPoint ChargingPoint1 { get; }

        public ChargingPoint ChargingPoint2 { get; }

        public ChargingStationMultiPointSimulation(IDateTimeProvider dateTimeProvider, ILogger logger) : base(logger)
        {
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
            ChargingPoint1 = new ChargingPoint(_dateTimeProvider, _logger);
            ChargingPoint2 = new ChargingPoint(_dateTimeProvider, _logger);
        }

        [Timer(5)]
        public void OnTimer()
        {
            ChargingPoint1.Update();
            ChargingPoint2.Update();
            ChargingPoint1Output.Set(ChargingPoint1.ActivePowerConsuming > 0);
            ChargingPoint2Output.Set(ChargingPoint2.ActivePowerConsuming > 0);
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            ExternallyLockedInput.InputChanged += (_, value) =>
                                                  {
                                                      _logger.LogInformation("Externally locked state changed: {ExternallyLockedInput}", value);
                                                      IsExternallyLocked = value;
                                                      ChargingPoint1.SetExternallyLocked(value);
                                                      ChargingPoint2.SetExternallyLocked(value);
                                                  };
        }

        /// <inheritdoc />
        protected override void Starting()
        {
            ChargingPoint1.Start();
            ChargingPoint2.Start();
        }

        public class ChargingPoint : IControllableElectricityConsumer
        {
            private readonly IDateTimeProvider _dateTimeProvider;

            private readonly ILogger _logger;

            private bool _enableCharging;

            private bool _isExternallyLocked;

            private DateTime? _lastUpdateTime;

            private double _maximumActivePower = 10;

            private double _requestedActivePower;

            [ServiceProperty(Title = "Maximale Wirkleistung", Unit = "kW")]
            [Presentation(Group = PropertyGroup.Configuration)]
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
            [Presentation(Group = PropertyGroup.Configuration, Importance = Importance.Secondary)]
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
            [ServiceMeasuringPoint]
            [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
            public double ActivePowerConsuming { get; private set; }

            [Persistent]
            [ServiceProperty(Title = "Zählerstand Gesamtverbrauch Total", Unit = "kWh")]
            [ServiceMeasuringPoint(Kind = MeasuringPointKind.TotalIncreasing)]
            [Presentation(Group = PropertyGroup.Metric)]
            public double EnergyConsumedTotal { get; private set; }

            [ServiceProperty(Title = "Angeforderte Wirkleistung", Unit = "kW")]
            [Presentation(Group = PropertyGroup.Status)]
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
            [Presentation(Group = PropertyGroup.Status)]
            public double AllocatedActivePower { get; private set; }

            // CS8618: Metalama's [Observable] aspect injects a non-nullable PropertyChanged event that
            // the weaver wires up post-compilation; the compiler doesn't see that and complains. Safe to suppress.
#pragma warning disable CS8618
            public ChargingPoint(IDateTimeProvider dateTimeProvider, ILogger logger)
#pragma warning restore CS8618
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

            public void SetExternallyLocked(bool value)
            {
                _isExternallyLocked = value;
                UpdateRequestedPower();
            }

            public void Start()
            {
                this.SendStateUpdate(new ControllableElectricityConsumerContract.StateUpdate(RequestedActivePower));
            }

            public void Update()
            {
                var currentTime = _dateTimeProvider.UtcNow;
                if (_lastUpdateTime.HasValue)
                {
                    var newActivePower = Math.Min(AllocatedActivePower, RequestedActivePower);

                    var energyIncrement = EnergyCalculator.CalculateEnergyIncrement(ActivePowerConsuming, newActivePower, _lastUpdateTime.Value, currentTime);

                    EnergyConsumedTotal += energyIncrement;

                    _logger.LogDebug("Energy increment: {EnergyInc:F6} kWh", energyIncrement);

                    ActivePowerConsuming = newActivePower;

                    _logger.LogInformation("Active power: {Power:F3} kW, Total energy: {Energy:F3} kWh", ActivePowerConsuming, EnergyConsumedTotal);
                }

                _lastUpdateTime = currentTime;

                this.SendStateUpdate(new ControllableElectricityConsumerContract.GridEffectStateUpdate(ActivePowerConsuming));
            }

            private void UpdateRequestedPower()
            {
                RequestedActivePower = EnableCharging && !_isExternallyLocked ? MaximumActivePower : 0;
            }
        }
    }
}