using System;
using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.Utils;

namespace Vion.Examples.Energy.LogicBlocks
{
    [LogicBlock(Name = "Energiemanager Simulation", Icon = "dashboard-line")]
    public class EnergyManagerSimulation : LogicBlockBase,
                                           IObservableElectricitySupplierManager,
                                           IObservableElectricityConsumerManager,
                                           IControllableElectricityConsumerManager,
                                           IControllableElectricityBufferManager
    {
        private const double IntervalSeconds = 5;

        public enum Mode
        {
            [EnumLabel("Lastmanagement")]
            LoadManagement,

            [EnumLabel("Eigenverbrauchsoptimierung")]
            SelfConsumptionOptimization,

            [EnumLabel("Spitzenlastkappung")]
            PeakShaving,
        }

        private readonly Dictionary<InterfaceId, ControllableElectricityBufferContract.Command> _controllableElectricityBufferAllocations = [];

        private readonly Dictionary<InterfaceId, ControllableElectricityBufferContract.DataResponse> _controllableElectricityBuffersData = [];

        private readonly Dictionary<InterfaceId, ControllableElectricityBufferContract.StateUpdate> _controllableElectricityBuffersState = [];

        private readonly Dictionary<InterfaceId, ControllableElectricityConsumerContract.Command> _controllableElectricityConsumerAllocations = [];

        private readonly Dictionary<InterfaceId, ControllableElectricityConsumerContract.DataResponse> _controllableElectricityConsumersData = [];

        private readonly Dictionary<InterfaceId, ControllableElectricityConsumerContract.StateUpdate> _controllableElectricityConsumersState = [];

        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly Dictionary<InterfaceId, double> _gridEffectData = [];

        private readonly ILogger _logger;

        private readonly Dictionary<InterfaceId, ObservableElectricityConsumerContract.DataResponse> _observableElectricityConsumersData = [];

        private readonly Dictionary<InterfaceId, ObservableElectricitySupplierContract.DataResponse> _observableElectricitySuppliersData = [];

        private readonly Dictionary<InterfaceId, ObservableElectricitySupplierContract.StateUpdate> _observableElectricitySuppliersState = [];

        private DateTime? _lastDataRequestDataTime;

        private DateTime? _lastUpdateTimeEnergyFlows;

        private DateTime? _lastUpdateTimeVirtualGrid;

        [ServiceProperty(Title = "Modus")]
        [Presentation(Group = PropertyGroup.Configuration, Importance = Importance.Secondary)]
        public Mode ModeGlobal { get; set; } = Mode.LoadManagement;

        [ServiceProperty(Title = "Grenzwert Lastmanagement", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public double LoadManagementLimit { get; set; } = 100.0;

        [ServiceProperty(Title = "Grenzwert Spitzenlastkappung", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public double PeakShavingLimit { get; set; } = 50.0;

        [ServiceProperty(Title = "Grenzwert Eigenverbrauchsoptimierung", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public double SelfConsumptionLimit { get; set; } = 0.0;

        [ServiceProperty(Title = "Antwortzeit Timeout")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public TimeSpan RequestDataTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

        [ServiceProperty(Title = "Antwortzeit")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public TimeSpan RequestDataResponseTime { get; private set; }

        [ServiceProperty(Title = "Maximale Wirkleistung Erzeugung")]
        [Presentation(Group = PropertyGroup.Status)]
        public double PeakActivePowerSupplying { get; private set; }

        [ServiceProperty(Title = "Gewichteter Ladezustand", Unit = "%")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public double WeighedBufferStateOfCharge { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Netzbezug", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Netzbezug", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public double ActivePowerImporting { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Netzeinspeisung", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Netzeinspeisung", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public double ActivePowerExporting { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Erzeugung Total", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Erzeugung Total", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public double ActivePowerSupplying { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Verbrauch Total", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Verbrauch Total", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public double ActivePowerConsuming { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Laden Total", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Laden Total", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public double ActivePowerCharging { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Entladen Total", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Entladen Total", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public double ActivePowerDischarging { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Batterie -> Verbrauch", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Batterie -> Verbrauch", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ActivePowerBuffersToConsumers { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Batterie -> Netz", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Batterie -> Netz", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ActivePowerBuffersToGrid { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Netz -> Batterie", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Netz -> Batterie", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ActivePowerGridToBuffers { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Netz -> Verbrauch", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Netz -> Verbrauch", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ActivePowerGridToConsumers { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Erzeugung -> Batterie", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Erzeugung -> Batterie", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ActivePowerProducersToBuffers { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Erzeugung -> Verbraucher", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Erzeugung -> Verbraucher", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ActivePowerProducersToConsumers { get; private set; }

        [ServiceProperty(Title = "Wirkleistung Erzeugung -> Netz", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Erzeugung -> Netz", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ActivePowerProducersToGrid { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Netzbezug Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Netzbezug Total", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyImportTotal { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Netzeinspeisung Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Netzeinspeisung Total", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyExportTotal { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Erzeugung Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Erzeugung Total", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergySuppliedTotal { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Verbrauch Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Verbrauch Total", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyConsumedTotal { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Laden Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Laden Total", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyChargedTotal { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Entladen Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Entladen Total", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyDischargedTotal { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Batterie -> Verbrauch", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Batterie -> Verbrauch", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyBuffersToConsumers { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Batterie -> Netz", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Batterie -> Netz", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyBuffersToGrid { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Netz -> Batterie", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Netz -> Batterie", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyGridToBuffers { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Netz -> Verbrauch", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Netz -> Verbrauch", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyGridToConsumers { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Erzeugung -> Batterie", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Erzeugung -> Batterie", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyProducersToBuffers { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Erzeugung -> Verbrauch", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Erzeugung -> Verbrauch", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyProducersToConsumers { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Erzeugung -> Netz", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Erzeugung -> Netz", Unit = "kWh")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double EnergyProducersToGrid { get; private set; }

        // total consumption, production, buffer charge/discharge energy

        public EnergyManagerSimulation(IDateTimeProvider dateTimeProvider, ILogger logger) : base(logger)
        {
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public void HandleResponse(InterfaceId functionId, ControllableElectricityBufferContract.DataResponse response)
        {
            _controllableElectricityBuffersData[functionId] = response;
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, ControllableElectricityBufferContract.StateUpdate response)
        {
            _controllableElectricityBuffersState[functionId] = response;
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, ControllableElectricityBufferContract.GridEffectStateUpdate response)
        {
            _gridEffectData[functionId] = response.ActivePowerGridEffect;
        }

        /// <inheritdoc />
        public void HandleResponse(InterfaceId functionId, ControllableElectricityConsumerContract.DataResponse response)
        {
            _controllableElectricityConsumersData[functionId] = response;
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, ControllableElectricityConsumerContract.StateUpdate response)
        {
            _controllableElectricityConsumersState[functionId] = response;
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, ControllableElectricityConsumerContract.GridEffectStateUpdate response)
        {
            _gridEffectData[functionId] = response.ActivePowerGridEffect;
        }

        /// <inheritdoc />
        public void HandleResponse(InterfaceId functionId, ObservableElectricityConsumerContract.DataResponse response)
        {
            _observableElectricityConsumersData[functionId] = response;
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, ObservableElectricityConsumerContract.GridEffectStateUpdate response)
        {
            _gridEffectData[functionId] = response.ActivePowerGridEffect;
        }

        /// <inheritdoc />
        public void HandleResponse(InterfaceId functionId, ObservableElectricitySupplierContract.DataResponse response)
        {
            _observableElectricitySuppliersData[functionId] = response;
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, ObservableElectricitySupplierContract.StateUpdate response)
        {
            _observableElectricitySuppliersState[functionId] = response;
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, ObservableElectricitySupplierContract.GridEffectStateUpdate response)
        {
            _gridEffectData[functionId] = response.ActivePowerGridEffect;
        }

        [Timer(IntervalSeconds)]
        public void OnTimer()
        {
            SendDataRequests();
            var delay = Math.Clamp(RequestDataTimeout.TotalSeconds, 0.0, IntervalSeconds);
            InvokeSynchronizedAfter(Calculate, TimeSpan.FromSeconds(delay));
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }

        private void SendDataRequests()
        {
            _lastDataRequestDataTime = _dateTimeProvider.UtcNow;

            foreach (var id in this.GetLinkedObservableElectricitySuppliers())
            {
                this.SendRequest(id, new ObservableElectricitySupplierContract.DataRequest());
            }

            foreach (var id in this.GetLinkedObservableElectricityConsumers())
            {
                this.SendRequest(id, new ObservableElectricityConsumerContract.DataRequest());
            }

            foreach (var id in this.GetLinkedControllableElectricityConsumers())
            {
                this.SendRequest(id, new ControllableElectricityConsumerContract.DataRequest());
            }

            foreach (var id in this.GetLinkedControllableElectricityBuffers())
            {
                this.SendRequest(id, new ControllableElectricityBufferContract.DataRequest());
            }
        }

        private void SendAllocationCommands()
        {
            foreach (var id in this.GetLinkedControllableElectricityConsumers())
            {
                if (!_controllableElectricityConsumerAllocations.TryGetValue(id, out var allocation))
                {
                    _logger.LogWarning("No allocation found for controllable electricity consumer {Id}", id);
                }
                else
                {
                    this.SendCommand(id, allocation);
                }
            }

            foreach (var id in this.GetLinkedControllableElectricityBuffers())
            {
                if (!_controllableElectricityBufferAllocations.TryGetValue(id, out var allocation))
                {
                    _logger.LogWarning("No allocation found for controllable electricity buffer {Id}", id);
                }
                else
                {
                    this.SendCommand(id, _controllableElectricityBufferAllocations[id]);
                }
            }
        }

        private void Calculate()
        {
            CheckData();
            CalculateVirtualGrid();
            CalculateEnergyFlows();
            CalculateAllocations();
            CalculatePeakActivePower();
            CalculateWeighedStateOfCharge();
        }

        private void CheckData()
        {
            // check that all requested data is available
            var missingData = this.GetLinkedControllableElectricityBuffers()
                                  .Except(_controllableElectricityBuffersData.Keys)
                                  .Concat(this.GetLinkedControllableElectricityConsumers().Except(_controllableElectricityConsumersData.Keys))
                                  .Concat(this.GetLinkedObservableElectricityConsumers().Except(_observableElectricityConsumersData.Keys))
                                  .Concat(this.GetLinkedObservableElectricitySuppliers().Except(_observableElectricitySuppliersData.Keys))
                                  .ToList();
            if (missingData.Count > 0)
            {
                _logger.LogWarning("No data from linked interfaces: {MissingData}", string.Join(", ", missingData));
            }

            // find oldest timestamp in data dictionaries
            var oldestDataTime = new[]
                                 {
                                     _controllableElectricityBuffersData.Values.Select(v => v.Timestamp).DefaultIfEmpty(_dateTimeProvider.UtcNow).Min(),
                                     _controllableElectricityConsumersData.Values.Select(v => v.Timestamp).DefaultIfEmpty(_dateTimeProvider.UtcNow).Min(),
                                     _observableElectricityConsumersData.Values.Select(v => v.Timestamp).DefaultIfEmpty(_dateTimeProvider.UtcNow).Min(),
                                     _observableElectricitySuppliersData.Values.Select(v => v.Timestamp).DefaultIfEmpty(_dateTimeProvider.UtcNow).Min(),
                                 }.Min();

            if (_lastDataRequestDataTime.HasValue)
            {
                RequestDataResponseTime = oldestDataTime - _lastDataRequestDataTime.Value;
                if (RequestDataResponseTime > RequestDataTimeout)
                {
                    _logger.LogWarning("Stale data detected: {OldestDataTime:O} < {LastDataRequestTime:O}", oldestDataTime, _lastDataRequestDataTime.Value);
                }
            }
        }

        private void CalculateAllocations()
        {
            switch (ModeGlobal)
            {
                case Mode.LoadManagement:
                {
                    Allocate(LoadManagementLimit, 0.0);
                    break;
                }

                case Mode.SelfConsumptionOptimization:
                {
                    Allocate(SelfConsumptionLimit, SelfConsumptionLimit);
                    break;
                }
                case Mode.PeakShaving:
                {
                    Allocate(PeakShavingLimit, PeakShavingLimit);
                    break;
                }
                default: throw new ArgumentOutOfRangeException();
            }

            SendAllocationCommands();
        }

        // assumptions:
        // - everything is measured
        // - consumers request what they will consume
        // - consumers consume what they are allocated
        // - buffers charge/discharge what they are allocated
        private void Allocate(double gridLimitActivePower, double gridLimitBufferCharging)
        {
            // calculate buffer potential
            var maxBufferDischarging = _controllableElectricityBuffersState.Values.Select(v => v.MaximumActivePowerDischarging).Sum();
            var maxBufferCharging = _controllableElectricityBuffersState.Values.Select(v => v.MaximumActivePowerCharging).Sum();

            var notControllableConsuming = _observableElectricityConsumersData.Values.Select(v => v.ActivePowerConsuming).Sum();

            var availableAllocationWithoutBuffers = gridLimitActivePower + ActivePowerSupplying - notControllableConsuming;
            var availableAllocationWithBuffers = availableAllocationWithoutBuffers + maxBufferDischarging;

            // distribute to consumers
            var requestedAllocation = _controllableElectricityConsumersState.Values.Select(v => v.RequestedActivePower).Sum();
            var allocationRatio = requestedAllocation > 0 ? Math.Clamp(availableAllocationWithBuffers / requestedAllocation, 0, 1) : 0.0;
            var actualAllocation = requestedAllocation * allocationRatio;

            foreach (var (id, state) in _controllableElectricityConsumersState)
            {
                var allocatedActivePower = state.RequestedActivePower * allocationRatio;
                _controllableElectricityConsumerAllocations[id] = new ControllableElectricityConsumerContract.Command(allocatedActivePower);
            }

            // compensate under-consumption with buffers
            var remainingAfterAllocation = availableAllocationWithoutBuffers - actualAllocation;
            if (remainingAfterAllocation < 0)
            {
                var dischargingRatio = maxBufferDischarging > 0 ? Math.Clamp(-remainingAfterAllocation / maxBufferDischarging, 0, 1) : 0.0;
                foreach (var (id, state) in _controllableElectricityBuffersState)
                {
                    var allocatedDischargingActivePower = state.MaximumActivePowerDischarging * dischargingRatio;
                    _controllableElectricityBufferAllocations[id] = new ControllableElectricityBufferContract.Command(0.0, allocatedDischargingActivePower);
                }
            }
            else
            {
                // decide if buffers can charge with remaining power
                var availableForBufferCharging = gridLimitBufferCharging + ActivePowerSupplying - notControllableConsuming - actualAllocation;

                var chargingRatio = maxBufferCharging > 0 ? Math.Clamp(availableForBufferCharging / maxBufferCharging, 0, 1) : 0.0;
                foreach (var (id, state) in _controllableElectricityBuffersState)
                {
                    var allocatedChargingActivePower = state.MaximumActivePowerCharging * chargingRatio;
                    _controllableElectricityBufferAllocations[id] = new ControllableElectricityBufferContract.Command(allocatedChargingActivePower, 0.0);
                }
            }
        }

        private void CalculateVirtualGrid()
        {
            var currentTime = _dateTimeProvider.UtcNow;

            if (_lastUpdateTimeVirtualGrid.HasValue)
            {
                var activePowerGridSigned = _gridEffectData.Values.Sum();
                if (activePowerGridSigned > 0)
                {
                    ActivePowerImporting = activePowerGridSigned;
                    ActivePowerExporting = 0;
                }
                else
                {
                    ActivePowerImporting = 0;
                    ActivePowerExporting = activePowerGridSigned * -1.0;
                }

                var energyImportIncrement = EnergyCalculator.CalculateEnergyIncrement(ActivePowerImporting, ActivePowerImporting, _lastUpdateTimeVirtualGrid.Value, currentTime);
                EnergyImportTotal += energyImportIncrement;

                var energyExportIncrement = EnergyCalculator.CalculateEnergyIncrement(ActivePowerExporting, ActivePowerExporting, _lastUpdateTimeVirtualGrid.Value, currentTime);
                EnergyExportTotal += energyExportIncrement;
            }

            _lastUpdateTimeVirtualGrid = currentTime;
        }

        private void CalculateWeighedStateOfCharge()
        {
            // capacity weighted state of charge of all buffers
            var totalCapacity = _controllableElectricityBuffersState.Values.Select(v => v.Capacity).Sum();

            if (totalCapacity > 0)
            {
                var totalWeighedSoc = 0.0;
                foreach (var (id, state) in _controllableElectricityBuffersState)
                {
                    var soc = _controllableElectricityBuffersData.GetValueOrDefault(id).StateOfCharge;
                    var weight = state.Capacity / totalCapacity;
                    totalWeighedSoc += soc * weight;
                }

                WeighedBufferStateOfCharge = totalWeighedSoc;
            }
            else
            {
                WeighedBufferStateOfCharge = 0.0;
            }
        }

        private void CalculatePeakActivePower()
        {
            PeakActivePowerSupplying = _observableElectricitySuppliersState.Values.Select(v => v.PeakActivePower).Sum();
        }

        private void CalculateEnergyFlows()
        {
            var currentTime = _dateTimeProvider.UtcNow;
            if (_lastUpdateTimeEnergyFlows.HasValue)
            {
                // -----------------------------
                // Summarize base values
                // -----------------------------

                ActivePowerSupplying = _observableElectricitySuppliersData.Values.Select(v => v.ActivePowerSupplying).Sum();

                ActivePowerConsuming = _observableElectricityConsumersData.Values.Select(v => v.ActivePowerConsuming).Sum() +
                                       _controllableElectricityConsumersData.Values.Select(v => v.ActivePowerConsuming).Sum();

                ActivePowerCharging = _controllableElectricityBuffersData.Values.Select(v => v.ActivePowerCharging).Sum();
                ActivePowerDischarging = _controllableElectricityBuffersData.Values.Select(v => v.ActivePowerDischarging).Sum();

                // positive: charging, negative: discharging
                var bufferSumSigned = ActivePowerCharging - ActivePowerDischarging;

                // positive: importing from grid, negative: exporting to grid
                var gridSumSigned = ActivePowerImporting - ActivePowerExporting;

                if (gridSumSigned < 0) // grid export
                {
                    // buffer discharging and grid import
                    if (bufferSumSigned < 0)
                    {
                        // buffer discharging and grid export (unexpected case occurs with control loop error while discharging) case 4
                        if (-gridSumSigned > ActivePowerSupplying)
                        {
                            // grid export higher than production total (case 4.1)
                            ActivePowerProducersToConsumers = 0;
                            ActivePowerProducersToGrid = ActivePowerSupplying;
                            ActivePowerProducersToBuffers = 0;

                            ActivePowerBuffersToConsumers = -bufferSumSigned + gridSumSigned + ActivePowerSupplying;
                            ActivePowerBuffersToGrid = -gridSumSigned - ActivePowerSupplying;

                            ActivePowerGridToConsumers = 0;
                            ActivePowerGridToBuffers = 0;
                        }
                        else
                        {
                            // grid export smaller than production total (case 4.2)
                            ActivePowerProducersToConsumers = ActivePowerSupplying + gridSumSigned;
                            ActivePowerProducersToGrid = -gridSumSigned;
                            ActivePowerProducersToBuffers = 0;

                            ActivePowerBuffersToConsumers = -bufferSumSigned;
                            ActivePowerBuffersToGrid = 0;

                            ActivePowerGridToConsumers = 0;
                            ActivePowerGridToBuffers = 0;
                        }
                    }
                    else
                    {
                        // grid power below zero -> export power (case 3)
                        // MAX(): special case: if producer power higher than consumer power and producer is not linked to EMS -> negative P1 (not allowed for charts) -> set to zero (do not add unmeasured feed in power to P2)
                        ActivePowerProducersToConsumers = Math.Max(ActivePowerSupplying + gridSumSigned - bufferSumSigned, 0.0);
                        ActivePowerProducersToGrid = -gridSumSigned;
                        ActivePowerProducersToBuffers = bufferSumSigned;

                        ActivePowerBuffersToConsumers = 0;
                        ActivePowerBuffersToGrid = 0;
                        ActivePowerGridToConsumers = 0;
                        ActivePowerGridToBuffers = 0;
                    }
                }
                else if (bufferSumSigned < 0) // buffer discharging and grid import
                {
                    // buffer power below zero -> discharging buffers (case 1)
                    ActivePowerProducersToConsumers = ActivePowerSupplying;
                    ActivePowerProducersToGrid = 0;
                    ActivePowerProducersToBuffers = 0;

                    ActivePowerBuffersToConsumers = -bufferSumSigned;
                    ActivePowerBuffersToGrid = 0;

                    ActivePowerGridToConsumers = gridSumSigned;
                    ActivePowerGridToBuffers = 0;
                }
                else // buffer charging and grid import
                {
                    // grid import, battery charging (case 3) -> equation solve only with a few assumptions valid
                    ActivePowerProducersToGrid = 0;
                    ActivePowerBuffersToConsumers = 0;
                    ActivePowerBuffersToGrid = 0;
                    if (ActivePowerSupplying > bufferSumSigned)
                    {
                        // producer production higher than charging power buffer -> 100% charging from local production (assumption for priorisation between P1, P3 vs. N1, N2)
                        ActivePowerProducersToBuffers = bufferSumSigned;
                        ActivePowerGridToBuffers = 0;
                        ActivePowerProducersToConsumers = ActivePowerSupplying - ActivePowerProducersToBuffers;
                    }
                    else
                    {
                        // producer production lower than charging power buffer -> 100% of local production goes to buffer
                        ActivePowerProducersToBuffers = ActivePowerSupplying;
                        ActivePowerProducersToConsumers = 0;
                        ActivePowerGridToBuffers = bufferSumSigned - ActivePowerProducersToBuffers;
                    }

                    ActivePowerGridToConsumers = gridSumSigned - ActivePowerGridToBuffers;
                }

                // -----------------------------
                // Convert kW -> kWh
                // -----------------------------

                EnergySuppliedTotal += EnergyCalculator.CalculateEnergyIncrement(ActivePowerSupplying, ActivePowerSupplying, _lastUpdateTimeEnergyFlows.Value, currentTime);
                EnergyConsumedTotal += EnergyCalculator.CalculateEnergyIncrement(ActivePowerConsuming, ActivePowerConsuming, _lastUpdateTimeEnergyFlows.Value, currentTime);

                EnergyChargedTotal += EnergyCalculator.CalculateEnergyIncrement(ActivePowerCharging, ActivePowerCharging, _lastUpdateTimeEnergyFlows.Value, currentTime);
                EnergyDischargedTotal += EnergyCalculator.CalculateEnergyIncrement(ActivePowerDischarging, ActivePowerDischarging, _lastUpdateTimeEnergyFlows.Value, currentTime);

                EnergyProducersToConsumers +=
                    EnergyCalculator.CalculateEnergyIncrement(ActivePowerProducersToConsumers, ActivePowerProducersToConsumers, _lastUpdateTimeEnergyFlows.Value, currentTime);
                EnergyProducersToGrid +=
                    EnergyCalculator.CalculateEnergyIncrement(ActivePowerProducersToGrid, ActivePowerProducersToGrid, _lastUpdateTimeEnergyFlows.Value, currentTime);
                EnergyProducersToBuffers +=
                    EnergyCalculator.CalculateEnergyIncrement(ActivePowerProducersToBuffers, ActivePowerProducersToBuffers, _lastUpdateTimeEnergyFlows.Value, currentTime);
                EnergyBuffersToConsumers +=
                    EnergyCalculator.CalculateEnergyIncrement(ActivePowerBuffersToConsumers, ActivePowerBuffersToConsumers, _lastUpdateTimeEnergyFlows.Value, currentTime);
                EnergyBuffersToGrid += EnergyCalculator.CalculateEnergyIncrement(ActivePowerBuffersToGrid, ActivePowerBuffersToGrid, _lastUpdateTimeEnergyFlows.Value, currentTime);
                EnergyGridToConsumers +=
                    EnergyCalculator.CalculateEnergyIncrement(ActivePowerGridToConsumers, ActivePowerGridToConsumers, _lastUpdateTimeEnergyFlows.Value, currentTime);
                EnergyGridToBuffers += EnergyCalculator.CalculateEnergyIncrement(ActivePowerGridToBuffers, ActivePowerGridToBuffers, _lastUpdateTimeEnergyFlows.Value, currentTime);
            }

            _lastUpdateTimeEnergyFlows = currentTime;
        }
    }
}