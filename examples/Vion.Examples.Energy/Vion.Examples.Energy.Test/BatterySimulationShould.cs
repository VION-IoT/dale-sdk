using System;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;
using Moq;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.LogicBlocks;
using Xunit;

namespace Vion.Examples.Energy.Test
{
    public class BatterySimulationShould
    {
        private readonly Mock<IDateTimeProvider> _dateTimeMock = new();
        private readonly BatterySimulation _sut;
        private DateTime _currentTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public BatterySimulationShould()
        {
            _dateTimeMock.Setup(d => d.UtcNow).Returns(() => _currentTime);
            _sut = new BatterySimulation(_dateTimeMock.Object, LogicBlockTestHelper.CreateLoggerMock().Object);
            _sut.Capacity = 100; // 100 kWh
            _sut.InitializeForTest();
        }

        private void AdvanceTime(TimeSpan offset)
        {
            _currentTime += offset;
        }

        /// <summary>
        ///     Run two timer ticks to establish _lastUpdateTime and compute CurrentMaximumActivePower values.
        /// </summary>
        private void WarmUp()
        {
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        ///     Charge the battery to a target SoC by running the charging cycle.
        /// </summary>
        private void ChargeTo(double targetSocPercent)
        {
            WarmUp();
            var energyNeeded = _sut.Capacity * targetSocPercent / 100.0; // kWh
            var chargePower = _sut.MaximumActivePowerCharging; // kW
            var hoursNeeded = energyNeeded / chargePower;

            _sut.HandleCommand(new ControllableElectricityBufferContract.Command(chargePower, 0.0));
            AdvanceTime(TimeSpan.FromHours(hoursNeeded));
            _sut.OnTimer();
        }

        // --- HandleRequest ---

        [Fact]
        public void HandleRequest_ReturnCurrentValues()
        {
            var response = _sut.HandleRequest(new ControllableElectricityBufferContract.DataRequest());

            Assert.Equal(0.0, response.ActivePowerCharging);
            Assert.Equal(0.0, response.ActivePowerDischarging);
            Assert.Equal(0.0, response.StateOfCharge);
        }

        // --- HandleCommand ---

        [Fact]
        public void HandleCommand_SetChargingPower()
        {
            ChargeTo(50.0);

            _sut.HandleCommand(new ControllableElectricityBufferContract.Command(5.0, 3.0));

            Assert.Equal(5.0, _sut.ActivePowerCharging);
            Assert.Equal(3.0, _sut.ActivePowerDischarging);
        }

        [Fact]
        public void HandleCommand_ClampToMaximumPower()
        {
            ChargeTo(50.0);

            _sut.HandleCommand(new ControllableElectricityBufferContract.Command(99.0, 99.0));

            Assert.Equal(_sut.MaximumActivePowerCharging, _sut.ActivePowerCharging);
            Assert.Equal(_sut.MaximumActivePowerDischarging, _sut.ActivePowerDischarging);
        }

        [Fact]
        public void HandleCommand_ClampDischargingToZeroWhenEmpty()
        {
            WarmUp();

            _sut.HandleCommand(new ControllableElectricityBufferContract.Command(5.0, 5.0));

            Assert.Equal(5.0, _sut.ActivePowerCharging); // can charge
            Assert.Equal(0.0, _sut.ActivePowerDischarging); // can't discharge empty battery
        }

        // --- OnTimer: energy integration ---

        [Fact]
        public void OnTimer_IntegrateChargingEnergy()
        {
            WarmUp();
            _sut.HandleCommand(new ControllableElectricityBufferContract.Command(10.0, 0.0));

            AdvanceTime(TimeSpan.FromHours(1));
            _sut.OnTimer();

            // 10 kW × 1 hour = 10 kWh
            Assert.Equal(10.0, _sut.EnergyChargedTotal, 1);
        }

        [Fact]
        public void OnTimer_UpdateStateOfCharge()
        {
            WarmUp();
            _sut.HandleCommand(new ControllableElectricityBufferContract.Command(10.0, 0.0));

            AdvanceTime(TimeSpan.FromHours(1));
            _sut.OnTimer();

            // 10 kWh into 100 kWh battery → 10%
            Assert.Equal(10.0, _sut.StateOfCharge, 1);
        }

        // --- OnTimer: SoC clamping ---

        [Fact]
        public void OnTimer_ClampStateOfChargeAt100Percent()
        {
            _sut.Capacity = 10; // small battery
            WarmUp();

            _sut.HandleCommand(new ControllableElectricityBufferContract.Command(10.0, 0.0));
            AdvanceTime(TimeSpan.FromHours(2)); // 20 kWh into 10 kWh battery
            _sut.OnTimer();

            Assert.Equal(100.0, _sut.StateOfCharge);
        }

        [Fact]
        public void OnTimer_ClampStateOfChargeAt0Percent()
        {
            ChargeTo(10.0);

            _sut.HandleCommand(new ControllableElectricityBufferContract.Command(0.0, 10.0));
            AdvanceTime(TimeSpan.FromHours(10)); // discharge far beyond capacity
            _sut.OnTimer();

            Assert.Equal(0.0, _sut.StateOfCharge);
        }

        // --- OnTimer: power limits based on SoC ---

        [Fact]
        public void OnTimer_DisableChargingWhenFull()
        {
            _sut.Capacity = 10;
            WarmUp();

            _sut.HandleCommand(new ControllableElectricityBufferContract.Command(10.0, 0.0));
            AdvanceTime(TimeSpan.FromHours(2));
            _sut.OnTimer();

            Assert.Equal(100.0, _sut.StateOfCharge);
            Assert.Equal(0.0, _sut.CurrentMaximumActivePowerCharging);
            Assert.Equal(_sut.MaximumActivePowerDischarging, _sut.CurrentMaximumActivePowerDischarging);
        }

        [Fact]
        public void OnTimer_DisableDischargingWhenEmpty()
        {
            WarmUp();
            // SoC is 0 (default), after WarmUp it stays 0

            Assert.Equal(0.0, _sut.StateOfCharge);
            Assert.Equal(_sut.MaximumActivePowerCharging, _sut.CurrentMaximumActivePowerCharging);
            Assert.Equal(0.0, _sut.CurrentMaximumActivePowerDischarging);
        }
    }
}
