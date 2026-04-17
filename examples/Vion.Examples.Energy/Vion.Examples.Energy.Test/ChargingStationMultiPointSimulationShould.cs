using System;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;
using Moq;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.LogicBlocks;
using Xunit;

namespace Vion.Examples.Energy.Test
{
    public class ChargingStationMultiPointSimulationShould
    {
        private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
        private readonly ChargingStationMultiPointSimulation _sut;
        private DateTime _currentTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public ChargingStationMultiPointSimulationShould()
        {
            _dateTimeMock.Setup(d => d.UtcNow).Returns(() => _currentTime);
            _sut = new ChargingStationMultiPointSimulation(_dateTimeMock.Object, LogicBlockTestHelper.CreateLoggerMock().Object);
            _sut.InitializeForTest();
        }

        private void AdvanceTime(TimeSpan offset)
        {
            _currentTime += offset;
        }

        // --- ChargingPoint independence ---

        [Fact]
        public void ChargingPoints_AreIndependent()
        {
            _sut.ChargingPoint1.EnableCharging = true;
            _sut.ChargingPoint2.EnableCharging = false;

            Assert.Equal(_sut.ChargingPoint1.MaximumActivePower, _sut.ChargingPoint1.RequestedActivePower);
            Assert.Equal(0.0, _sut.ChargingPoint2.RequestedActivePower);
        }

        // --- HandleCommand per point ---

        [Fact]
        public void ChargingPoint_HandleCommand_SetAllocatedPower()
        {
            _sut.ChargingPoint1.HandleCommand(new ControllableElectricityConsumerContract.Command(7.0));
            _sut.ChargingPoint2.HandleCommand(new ControllableElectricityConsumerContract.Command(3.0));

            Assert.Equal(7.0, _sut.ChargingPoint1.AllocatedActivePower);
            Assert.Equal(3.0, _sut.ChargingPoint2.AllocatedActivePower);
        }

        // --- HandleRequest per point ---

        [Fact]
        public void ChargingPoint_HandleRequest_ReturnCurrentValues()
        {
            var response1 = _sut.ChargingPoint1.HandleRequest(new ControllableElectricityConsumerContract.DataRequest());
            var response2 = _sut.ChargingPoint2.HandleRequest(new ControllableElectricityConsumerContract.DataRequest());

            Assert.Equal(0.0, response1.ActivePowerConsuming);
            Assert.Equal(0.0, response2.ActivePowerConsuming);
        }

        // --- OnTimer delegates to both points ---

        [Fact]
        public void OnTimer_UpdateBothChargingPoints()
        {
            _sut.ChargingPoint1.EnableCharging = true;
            _sut.ChargingPoint1.HandleCommand(new ControllableElectricityConsumerContract.Command(5.0));
            _sut.ChargingPoint2.EnableCharging = true;
            _sut.ChargingPoint2.HandleCommand(new ControllableElectricityConsumerContract.Command(8.0));

            _sut.OnTimer(); // first tick
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer(); // second tick computes power

            Assert.Equal(5.0, _sut.ChargingPoint1.ActivePowerConsuming);
            Assert.Equal(8.0, _sut.ChargingPoint2.ActivePowerConsuming);
        }

        [Fact]
        public void OnTimer_IntegrateEnergyIndependently()
        {
            _sut.ChargingPoint1.EnableCharging = true;
            _sut.ChargingPoint1.HandleCommand(new ControllableElectricityConsumerContract.Command(10.0));
            _sut.ChargingPoint2.EnableCharging = false;

            // Two ticks to ramp ActivePowerConsuming to steady state
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer();

            var energyBefore = _sut.ChargingPoint1.EnergyConsumedTotal;
            AdvanceTime(TimeSpan.FromHours(1));
            _sut.OnTimer();

            Assert.Equal(10.0, _sut.ChargingPoint1.EnergyConsumedTotal - energyBefore, 1);
            Assert.Equal(0.0, _sut.ChargingPoint2.ActivePowerConsuming);
        }
    }
}
