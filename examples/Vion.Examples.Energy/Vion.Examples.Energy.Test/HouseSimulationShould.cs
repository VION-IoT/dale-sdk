using System;
using Vion.Dale.Sdk.TestKit;
using Microsoft.Extensions.Time.Testing;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.LogicBlocks;
using Xunit;

namespace Vion.Examples.Energy.Test
{
    public class HouseSimulationShould
    {
        // Anchor at midnight so individual tests can SetUtcNow to any time-of-day on this date
        // without violating FakeTimeProvider's monotonic-clock invariant.
        private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        private readonly HouseSimulation _sut;

        public HouseSimulationShould()
        {
            _sut = new HouseSimulation(_timeProvider, LogicBlockTestHelper.CreateLoggerMock().Object);
            _sut.InitializeForTest();
        }

        private void AdvanceTime(TimeSpan offset)
        {
            _timeProvider.Advance(offset);
        }

        // --- HandleRequest ---

        [Fact]
        public void HandleRequest_ReturnCurrentValues()
        {
            var response = _sut.HandleRequest(new ObservableElectricityConsumerContract.DataRequest());

            Assert.Equal(0.0, response.ActivePowerConsuming);
            Assert.Equal(0.0, response.EnergyConsumedTotal);
        }

        // --- OnTimer: power calculation ---

        [Fact]
        public void OnTimer_CalculatePowerAboveBaseConsumption()
        {
            _sut.OnTimer(); // first tick
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer(); // second tick calculates power

            // Power should always be at least base consumption
            Assert.True(_sut.ActivePowerConsuming >= _sut.BaseConsumption);
        }

        [Fact]
        public void OnTimer_MorningPeakHigherThanNight()
        {
            // Set time to 3 AM (low consumption, Europe/Zurich = UTC+1)
            _timeProvider.SetUtcNow(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero)); // 3 AM local
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer();
            var nightPower = _sut.ActivePowerConsuming;

            // Set time to 7 AM local (morning peak, Europe/Zurich = UTC+1)
            _timeProvider.SetUtcNow(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero)); // 7 AM local
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer();
            var morningPower = _sut.ActivePowerConsuming;

            Assert.True(morningPower > nightPower,
                $"Morning power ({morningPower:F3} kW) should exceed night power ({nightPower:F3} kW)");
        }

        [Fact]
        public void OnTimer_EveningPeakHigherThanNight()
        {
            // Set time to 3 AM
            _timeProvider.SetUtcNow(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero));
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer();
            var nightPower = _sut.ActivePowerConsuming;

            // Set time to 6 PM local (evening cooking peak, UTC+1)
            _timeProvider.SetUtcNow(new DateTimeOffset(2026, 1, 1, 17, 0, 0, TimeSpan.Zero));
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer();
            var eveningPower = _sut.ActivePowerConsuming;

            Assert.True(eveningPower > nightPower,
                $"Evening power ({eveningPower:F3} kW) should exceed night power ({nightPower:F3} kW)");
        }

        // --- OnTimer: energy integration ---

        [Fact]
        public void OnTimer_IntegrateEnergyOverTime()
        {
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromHours(1));
            _sut.OnTimer();

            // After 1 hour, some energy should have been consumed
            Assert.True(_sut.EnergyConsumedTotal > 0);
        }

        // --- Configuration ---

        [Fact]
        public void BaseConsumption_AffectsPowerCalculation()
        {
            _sut.BaseConsumption = 1.0;
            _sut.MorningPeakConsumption = 0;
            _sut.EveningCookingPeakConsumption = 0;
            _sut.EventingHeatingPeakConsumption = 0;

            // Use a time far from any peak (3 AM local)
            _timeProvider.SetUtcNow(new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero));
            _sut.OnTimer();
            AdvanceTime(TimeSpan.FromSeconds(5));
            _sut.OnTimer();

            // With no peaks and 3 AM, power should be very close to base consumption
            Assert.Equal(1.0, _sut.ActivePowerConsuming, 2);
        }
    }
}
