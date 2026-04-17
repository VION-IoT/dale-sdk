using System;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;
using Moq;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.LogicBlocks;
using Xunit;

namespace Vion.Examples.Energy.Test
{
    public class EnergyManagerSimulationShould
    {
        public EnergyManagerSimulationShould()
        {
            _dateTimeMock.Setup(d => d.UtcNow).Returns(() => _currentTime);
            _sut = new EnergyManagerSimulation(_dateTimeMock.Object, LogicBlockTestHelper.CreateLoggerMock().Object);
            _testContext = _sut.CreateTestContext()
                               .WithLogicInterfaceMapping<IObservableElectricitySupplierManager>(PvId)
                               .WithLogicInterfaceMapping<IObservableElectricityConsumerManager>(HouseId)
                               .WithLogicInterfaceMapping<IControllableElectricityConsumerManager>(ChargingStationId)
                               .WithLogicInterfaceMapping<IControllableElectricityBufferManager>(BatteryId)
                               .Build();
        }

        // Linked interface IDs (simulated remote blocks)
        private static readonly InterfaceId PvId = new("pv-block", "IObservableElectricitySupplier");

        private static readonly InterfaceId HouseId = new("house-block", "IObservableElectricityConsumer");

        private static readonly InterfaceId ChargingStationId = new("charging-block", "IControllableElectricityConsumer");

        private static readonly InterfaceId BatteryId = new("battery-block", "IControllableElectricityBuffer");

        private readonly Mock<IDateTimeProvider> _dateTimeMock = new();

        private readonly EnergyManagerSimulation _sut;

        private readonly LogicBlockTestContext<EnergyManagerSimulation> _testContext;

        private DateTime _currentTime = new(2026,
                                            1,
                                            1,
                                            12,
                                            0,
                                            0,
                                            DateTimeKind.Utc);

        private void AdvanceTime(TimeSpan offset)
        {
            _currentTime += offset;
        }

        /// <summary>
        ///     Feed state updates and data responses for all linked interfaces, then run OnTimer + FlushPendingActions.
        /// </summary>
        private void RunCycle(double pvPower = 0,
                              double houseConsumption = 0,
                              double chargingStationConsumption = 0,
                              double chargingStationRequested = 0,
                              double batteryCharging = 0,
                              double batteryDischarging = 0,
                              double batterySoc = 50,
                              double batteryMaxCharging = 10,
                              double batteryMaxDischarging = 10,
                              double batteryCapacity = 100)
        {
            var now = _currentTime;

            // State updates (normally sent proactively by linked blocks)
            _sut.HandleStateUpdate(PvId, new ObservableElectricitySupplierContract.StateUpdate(pvPower));
            _sut.HandleStateUpdate(ChargingStationId, new ControllableElectricityConsumerContract.StateUpdate(chargingStationRequested));
            _sut.HandleStateUpdate(BatteryId, new ControllableElectricityBufferContract.StateUpdate(batteryMaxCharging, batteryMaxDischarging, batteryCapacity));

            // Grid effect state updates (each block reports its grid contribution)
            _sut.HandleStateUpdate(PvId, new ObservableElectricitySupplierContract.GridEffectStateUpdate(-pvPower));
            _sut.HandleStateUpdate(HouseId, new ObservableElectricityConsumerContract.GridEffectStateUpdate(houseConsumption));
            _sut.HandleStateUpdate(ChargingStationId, new ControllableElectricityConsumerContract.GridEffectStateUpdate(chargingStationConsumption));
            _sut.HandleStateUpdate(BatteryId, new ControllableElectricityBufferContract.GridEffectStateUpdate(batteryCharging - batteryDischarging));

            // Trigger timer: sends data requests + schedules Calculate
            _sut.OnTimer();

            // Feed data responses (as if linked blocks responded to the data requests)
            _sut.HandleResponse(PvId, new ObservableElectricitySupplierContract.DataResponse(now, pvPower, 0));
            _sut.HandleResponse(HouseId, new ObservableElectricityConsumerContract.DataResponse(now, houseConsumption, 0));
            _sut.HandleResponse(ChargingStationId, new ControllableElectricityConsumerContract.DataResponse(now, chargingStationConsumption, 0));
            _sut.HandleResponse(BatteryId,
                                new ControllableElectricityBufferContract.DataResponse(now,
                                                                                       batteryCharging,
                                                                                       batteryDischarging,
                                                                                       0,
                                                                                       0,
                                                                                       batterySoc));

            // Execute the delayed Calculate()
            _testContext.FlushPendingActions();
        }

        // --- Energy flow: buffer discharging + grid import (case 1) ---

        [Fact]
        public void Calculate_BufferDischarging_ProducersToConsumersAndBuffersToConsumers()
        {
            // PV=5, house=3, charging station consuming=5, battery discharging=2, grid importing=1
            RunCycle(5, 3, 5, batteryDischarging: 2);
            AdvanceTime(TimeSpan.FromSeconds(5));
            RunCycle(5, 3, 5, batteryDischarging: 2);

            Assert.Equal(5.0, _sut.ActivePowerProducersToConsumers);
            Assert.Equal(2.0, _sut.ActivePowerBuffersToConsumers);
            Assert.Equal(1.0, _sut.ActivePowerGridToConsumers);
            Assert.Equal(0.0, _sut.ActivePowerProducersToGrid);
        }

        // --- Energy flow: consumption exceeds production → grid import (case 1/2) ---

        [Fact]
        public void Calculate_ConsumptionExceedsProduction_GridImport()
        {
            // PV produces 3, house consumes 8, grid imports 5
            RunCycle(3, 8);
            AdvanceTime(TimeSpan.FromSeconds(5));
            RunCycle(3, 8);

            Assert.Equal(3.0, _sut.ActivePowerSupplying);
            Assert.Equal(8.0, _sut.ActivePowerConsuming);
            Assert.Equal(5.0, _sut.ActivePowerImporting);
            Assert.Equal(0.0, _sut.ActivePowerExporting);
        }

        // --- Energy flow: buffer charging from production (case 3) ---

        [Fact]
        public void Calculate_ExcessProduction_BufferCharges()
        {
            // PV=10, house=3, battery charging=4, grid exports=3
            RunCycle(10, 3, batteryCharging: 4);
            AdvanceTime(TimeSpan.FromSeconds(5));
            RunCycle(10, 3, batteryCharging: 4);

            Assert.Equal(4.0, _sut.ActivePowerProducersToBuffers);
            Assert.Equal(3.0, _sut.ActivePowerProducersToGrid);
        }

        // --- Virtual grid: energy integration ---

        [Fact]
        public void Calculate_IntegrateGridEnergy()
        {
            // First cycle establishes _lastUpdateTimeVirtualGrid
            RunCycle(houseConsumption: 5.0);

            AdvanceTime(TimeSpan.FromHours(1));
            RunCycle(houseConsumption: 5.0);

            // 5 kW import × 1 hour = 5 kWh
            Assert.Equal(5.0, _sut.EnergyImportTotal, 1);
            Assert.Equal(0.0, _sut.EnergyExportTotal);
        }

        // --- Allocation: LoadManagement mode ---

        [Fact]
        public void Calculate_LoadManagement_AllocateWithinGridLimit()
        {
            _sut.ModeGlobal = EnergyManagerSimulation.Mode.LoadManagement;
            _sut.LoadManagementLimit = 10.0;

            // Warmup cycle to initialize energy flow timestamps
            RunCycle(houseConsumption: 3, chargingStationRequested: 5);
            AdvanceTime(TimeSpan.FromSeconds(5));
            _testContext.ClearRecordedMessages();

            // Charging station requests 5 kW, PV=0, house=3 (observable, not controllable)
            // Available = gridLimit(10) + supply(0) - nonControllableConsuming(3) + maxBufferDischarge(10) = 17
            // Requested = 5, ratio = min(17/5, 1) = 1.0 → full allocation
            RunCycle(houseConsumption: 3, chargingStationRequested: 5);

            _testContext.VerifySendCommand<ControllableElectricityConsumerContract.Command>(ChargingStationId, cmd => Assert.Equal(5.0, cmd.AllocatedActivePower));
        }

        // --- Allocation: buffer charging when surplus ---

        [Fact]
        public void Calculate_LoadManagement_BufferChargesWithSurplus()
        {
            _sut.ModeGlobal = EnergyManagerSimulation.Mode.LoadManagement;
            _sut.LoadManagementLimit = 10.0;

            // Warmup cycle
            RunCycle(8, 3, chargingStationRequested: 0);
            AdvanceTime(TimeSpan.FromSeconds(5));
            _testContext.ClearRecordedMessages();

            // PV=8, house=3, no controllable consumers requesting
            // remainingAfterAllocation = gridLimit(10) + supply(8) - nonControllable(3) - allocated(0) = 15 > 0
            // → buffer can charge
            // availableForBufferCharging = gridLimitBufferCharging(0) + supply(8) - nonControllable(3) - allocated(0) = 5
            // chargingRatio = min(5/10, 1) = 0.5 → each buffer charges at 50% max
            RunCycle(8, 3, chargingStationRequested: 0);

            _testContext.VerifySendCommand<ControllableElectricityBufferContract.Command>(BatteryId,
                                                                                          cmd =>
                                                                                          {
                                                                                              Assert.Equal(5.0, cmd.AllocatedActivePowerCharging, 1);
                                                                                              Assert.Equal(0.0, cmd.AllocatedActivePowerDischarging);
                                                                                          });
        }

        [Fact]
        public void Calculate_LoadManagement_CurtailWhenOverLimit()
        {
            _sut.ModeGlobal = EnergyManagerSimulation.Mode.LoadManagement;
            _sut.LoadManagementLimit = 5.0;

            // Warmup cycle
            RunCycle(houseConsumption: 8, chargingStationRequested: 20);
            AdvanceTime(TimeSpan.FromSeconds(5));
            _testContext.ClearRecordedMessages();

            // Charging station requests 20, PV=0, house=8
            // Available = gridLimit(5) + supply(0) - nonControllable(8) + maxBufferDischarge(10) = 7
            // ratio = 7/20 = 0.35 → allocated = 7
            RunCycle(houseConsumption: 8, chargingStationRequested: 20);

            _testContext.VerifySendCommand<ControllableElectricityConsumerContract.Command>(ChargingStationId, cmd => Assert.Equal(7.0, cmd.AllocatedActivePower, 1));
        }

        // --- PeakActivePower aggregation ---

        [Fact]
        public void Calculate_PeakActivePowerFromSupplierState()
        {
            RunCycle(15);

            Assert.Equal(15.0, _sut.PeakActivePowerSupplying);
        }

        // --- Energy flow: production with grid export (case 3) ---

        [Fact]
        public void Calculate_ProductionExceedsConsumption_GridExport()
        {
            // PV produces 10, house consumes 3, no battery, grid exports 7
            RunCycle(10, 3);
            AdvanceTime(TimeSpan.FromSeconds(5));
            RunCycle(10, 3);

            Assert.Equal(10.0, _sut.ActivePowerSupplying);
            Assert.Equal(3.0, _sut.ActivePowerConsuming);
            Assert.Equal(7.0, _sut.ActivePowerExporting);
            Assert.Equal(0.0, _sut.ActivePowerImporting);
        }

        // --- WeighedStateOfCharge ---

        [Fact]
        public void Calculate_WeighedStateOfCharge()
        {
            RunCycle(batterySoc: 60, batteryCapacity: 100);

            Assert.Equal(60.0, _sut.WeighedBufferStateOfCharge, 1);
        }

        // --- Basic two-phase pattern ---

        [Fact]
        public void OnTimer_FlushPendingActions_ExecuteCalculate()
        {
            // First cycle initializes timestamps; second cycle computes energy flows
            RunCycle(8.0, 5.0);
            AdvanceTime(TimeSpan.FromSeconds(5));
            RunCycle(8.0, 5.0);

            Assert.Equal(8.0, _sut.ActivePowerSupplying);
        }
    }
}