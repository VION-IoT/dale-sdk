using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Examples.FunctionInterfaces;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Examples.LogicBlocks
{
    public enum ChargingStationConnectionState
    {
        [EnumValueInfo("Unbekannt")]
        [StatusSeverity(StatusSeverity.Neutral)]
        Unknown,

        [EnumValueInfo("Verbinden...")]
        [StatusSeverity(StatusSeverity.Warning)]
        Connecting,

        [EnumValueInfo("Verbunden")]
        [StatusSeverity(StatusSeverity.Success)]
        Connected,

        [EnumValueInfo("Getrennt")]
        [StatusSeverity(StatusSeverity.Error)]
        Disconnected,
    }

    [ServiceInterface]
    public interface IChargingStationService
    {
        [ServiceProperty]
        public bool Foo { get; set; }
    }

    [Service("ChargingStationMultiPointSimulation")]
    [LogicBlockInfo("Ladestation Simulation", "ev-station")]
    public class ChargingStationMultiPointSimulation : LogicBlockBase, IChargingStationService, IPing, IToggleable
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly ILogger _logger;

        //[Service("DefaultChargingPoint")]
        //[Interface(typeof(IPing), "Ping1", "DefaultChargingPoint")]
        //[Interface(typeof(IToggleable), "Toggleable1", "DefaultChargingPointXX")]
        public ChargingPoint ChargingPoint1 { get; } = new();

        public ChargingPoint ChargingPoint2 { get; } = new();

        [ServiceProperty]
        [ServiceMeasuringPoint]
        public int CounterTotal
        {
            // This should trigger a Metalama warning because GetCounter() is a method call
            // that Metalama cannot automatically track for property change notifications.
            // Metalama can track direct property accesses like ChargingPoint1.Counter,
            // but method calls are opaque to the dependency analysis.
            //get => GetChargingPoint1Counter() + GetChargingPoint2Counter();
            get => Math.Clamp(ChargingPoint1.Counter + ChargingPoint2.Counter, 0, 1);
        }

        public ChargingStationMultiPointSimulation(IDateTimeProvider dateTimeProvider, ILogger logger) : base(logger)
        {
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public bool Foo { get; set; }

        /// <inheritdoc />
        public void HandleResponse(InterfaceId functionId, PingPong.PongResponse response)
        {
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, Toggling.TogglePressed response)
        {
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, Toggling.ToggleReleased response)
        {
        }

        [Timer(5)]
        public void OnTimer()
        {
            Foo = !Foo;
            ChargingPoint1.Update(_dateTimeProvider, _logger);
            ChargingPoint2.Update(_dateTimeProvider, _logger);
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }

        /// <inheritdoc />
        protected override void Starting()
        {
            ChargingPoint1.Start();
            ChargingPoint2.Start();
        }

        private int GetChargingPoint1Counter()
        {
            return ChargingPoint1.Counter;
        }

        private int GetChargingPoint2Counter()
        {
            return ChargingPoint2.Counter;
        }

        public class ChargingPoint : IPing, IToggleable, IChargingStationService
        {
            private bool _enableCharging;

            private DateTime? _lastUpdateTime;

            private double _maximumActivePower = 10;

            [ServiceProperty]
            [ServiceMeasuringPoint]
            public int Counter { get; set; }

            [ServiceProperty(Unit = "kW")]
            [Category(PropertyCategory.Configuration)]
            [Display(group: "Energy")]
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

            [ServiceProperty]
            [Category(PropertyCategory.Configuration)]
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

            [ServiceProperty(Unit = "kW")]
            [ServiceMeasuringPoint(Unit = "kW")]
            [Importance(Importance.Primary)]
            [Display(group: "Energy")]
            public double ActivePowerConsuming { get; private set; }

            [Persistent]
            [ServiceProperty(Unit = "kWh")]
            [ServiceMeasuringPoint(Unit = "kWh")]
            [Importance(Importance.Secondary)]
            [Display(group: "Energy")]
            public double EnergyConsumedTotal { get; private set; }

            [ServiceProperty(Unit = "kW")]
            public double RequestedActivePower { get; private set; }

            [ServiceProperty(Unit = "kW")]
            public double AllocatedActivePower { get; private set; }

            [ServiceProperty]
            [StatusIndicator]
            public ChargingStationConnectionState ConnectionState { get; private set; } = ChargingStationConnectionState.Unknown;

            /// <inheritdoc />
            public bool Foo { get; set; }

            /// <inheritdoc />
            public void HandleResponse(InterfaceId functionId, PingPong.PongResponse response)
            {
            }

            /// <inheritdoc />
            public void HandleStateUpdate(InterfaceId functionId, Toggling.TogglePressed response)
            {
            }

            /// <inheritdoc />
            public void HandleStateUpdate(InterfaceId functionId, Toggling.ToggleReleased response)
            {
            }

            public void Start()
            {
            }

            public void Update(IDateTimeProvider dateTimeProvider, ILogger logger)
            {
                Counter++;
            }

            private void UpdateRequestedPower()
            {
                RequestedActivePower = EnableCharging ? MaximumActivePower : 0;
            }
        }
    }
}
