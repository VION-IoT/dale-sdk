using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Examples.FunctionInterfaces;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Examples.LogicBlocks
{
    public enum ChargingStationConnectionState
    {
        [EnumLabel("Unbekannt")]
        [Severity(StatusSeverity.Neutral)]
        Unknown,

        [EnumLabel("Verbinden...")]
        [Severity(StatusSeverity.Warning)]
        Connecting,

        [EnumLabel("Verbunden")]
        [Severity(StatusSeverity.Success)]
        Connected,

        [EnumLabel("Getrennt")]
        [Severity(StatusSeverity.Error)]
        Disconnected,
    }

    [ServiceInterface]
    public interface IChargingStationService
    {
        [ServiceProperty]
        public bool Foo { get; set; }
    }

    [LogicBlock(Name = "Ladestation Simulation", Icon = "ev-station")]
    public class ChargingStationMultiPointSimulation : LogicBlockBase, IChargingStationService, IPing, IToggleable
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly ILogger _logger;

        //[Service("DefaultChargingPoint")]
        //[LogicBlockInterfaceBinding(typeof(IPing), Identifier = "Ping1", DefaultName = "DefaultChargingPoint")]
        //[LogicBlockInterfaceBinding(typeof(IToggleable), Identifier = "Toggleable1", DefaultName = "DefaultChargingPointXX")]
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
            [Presentation(Group = PropertyGroup.Metric)]
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
            [Presentation(Group = PropertyGroup.Configuration)]
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
            [Presentation(Importance = Importance.Primary, Group = PropertyGroup.Metric)]
            public double ActivePowerConsuming { get; private set; }

            [Persistent]
            [ServiceProperty(Unit = "kWh")]
            [ServiceMeasuringPoint(Unit = "kWh")]
            [Presentation(Importance = Importance.Secondary, Group = PropertyGroup.Metric)]
            public double EnergyConsumedTotal { get; private set; }

            [ServiceProperty(Unit = "kW")]
            public double RequestedActivePower { get; private set; }

            [ServiceProperty(Unit = "kW")]
            public double AllocatedActivePower { get; private set; }

            [ServiceProperty]
            [Presentation(StatusIndicator = true)]
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
