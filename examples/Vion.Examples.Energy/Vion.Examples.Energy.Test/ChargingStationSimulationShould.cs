using System;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.LogicBlocks;
using Xunit;

namespace Vion.Examples.Energy.Test
{
    public class ChargingStationSimulationShould
    {
        public ChargingStationSimulationShould()
        {
            _sut = new ChargingStationSimulation(_timeProvider, LogicBlockTestHelper.CreateLoggerMock().Object);
            _sut.InitializeForTest();
        }

        private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026,
                                                                                 1,
                                                                                 1,
                                                                                 12,
                                                                                 0,
                                                                                 0,
                                                                                 TimeSpan.Zero));

        private readonly ChargingStationSimulation _sut;

        private void AdvanceTime(TimeSpan offset)
        {
            _timeProvider.Advance(offset);
        }

        [Fact]
        public void EnableCharging_False_RequestZeroPower()
        {
            _sut.EnableCharging = true;
            _sut.EnableCharging = false;

            Assert.Equal(0.0, _sut.RequestedActivePower);
        }

        // --- EnableCharging / RequestedActivePower ---

        [Fact]
        public void EnableCharging_True_RequestMaximumPower()
        {
            _sut.EnableCharging = true;

            Assert.Equal(_sut.MaximumActivePower, _sut.RequestedActivePower);
        }

        // --- HandleCommand ---

        [Fact]
        public void HandleCommand_SetAllocatedPower()
        {
            _sut.HandleCommand(new ControllableElectricityConsumerContract.Command(7.5));

            Assert.Equal(7.5, _sut.AllocatedActivePower);
        }

        // --- HandleRequest ---

        [Fact]
        public void HandleRequest_ReturnCurrentValues()
        {
            var response = _sut.HandleRequest(new ControllableElectricityConsumerContract.DataRequest());

            Assert.Equal(0.0, response.ActivePowerConsuming);
            Assert.Equal(0.0, response.EnergyConsumedTotal);
        }

        [Fact]
        public void MaximumActivePower_Changed_UpdateRequestedPower()
        {
            _sut.EnableCharging = true;
            _sut.MaximumActivePower = 22.0;

            Assert.Equal(22.0, _sut.RequestedActivePower);
        }

        // --- OnTimer ---

        [Fact]
        public void OnTimer_ConsumePowerLimitedByAllocation()
        {
            _sut.EnableCharging = true; // RequestedActivePower = 10
            _sut.HandleCommand(new ControllableElectricityConsumerContract.Command(5.0)); // Allocated = 5

            // First tick establishes _lastUpdateTime
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));

            // Second tick computes: min(Allocated=5, Requested=10) = 5
            _sut.OnTimer();

            Assert.Equal(5.0, _sut.ActivePowerConsuming);
        }

        [Fact]
        public void OnTimer_ConsumeZeroWhenChargingDisabled()
        {
            _sut.EnableCharging = false; // RequestedActivePower = 0
            _sut.HandleCommand(new ControllableElectricityConsumerContract.Command(10.0)); // Allocated = 10

            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer();

            // min(Allocated=10, Requested=0) = 0
            Assert.Equal(0.0, _sut.ActivePowerConsuming);
        }

        [Fact]
        public void OnTimer_IntegrateEnergyOverTime()
        {
            _sut.EnableCharging = true;
            _sut.HandleCommand(new ControllableElectricityConsumerContract.Command(10.0));

            // First two ticks establish _lastUpdateTime and ramp ActivePowerConsuming to 10
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer();

            // Now ActivePowerConsuming = 10, measure from here
            var energyBefore = _sut.EnergyConsumedTotal;
            AdvanceTime(TimeSpan.FromHours(1));
            _sut.OnTimer();

            // 10 kW × 1 hour = 10 kWh (trapezoidal with constant power)
            Assert.Equal(10.0, _sut.EnergyConsumedTotal - energyBefore, 1);
        }
    }
}