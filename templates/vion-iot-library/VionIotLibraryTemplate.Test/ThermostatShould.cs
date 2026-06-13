using Vion.Dale.Sdk.TestKit;
using Xunit;

namespace VionIotLibraryTemplate.Test
{
    public class ThermostatShould
    {
        public ThermostatShould()
        {
            _thermostat = new Thermostat(LogicBlockTestHelper.CreateLoggerMock().Object);
            _thermostat.InitializeForTest(); // Initialize the logic block for testing
        }

        private readonly Thermostat _thermostat;

        [Fact]
        public void Heat_WhenRoomIsBelowTheSetpoint()
        {
            // Arrange
            _thermostat.Mode = ThermostatMode.Auto;
            _thermostat.TargetTemperature = 25.0;
            _thermostat.CurrentTemperature = 20.0; // inject a cold reading (CurrentTemperature is writable too)

            // Act
            _thermostat.Tick();

            // Assert
            Assert.Equal(ThermostatStatus.Heating, _thermostat.Status);
            Assert.True(_thermostat.CurrentTemperature > 20.0, "the room should warm toward the setpoint");
        }

        [Fact]
        public void StayIdle_AndMeterNoEnergy_WhenOff()
        {
            // Arrange
            _thermostat.Mode = ThermostatMode.Off;
            _thermostat.CurrentTemperature = 10.0; // far below the setpoint, but Off must not act
            var energyBefore = _thermostat.EnergyUsedKwh;

            // Act
            _thermostat.Tick();

            // Assert
            Assert.Equal(ThermostatStatus.Idle, _thermostat.Status);
            Assert.Equal(energyBefore, _thermostat.EnergyUsedKwh);
        }
    }
}