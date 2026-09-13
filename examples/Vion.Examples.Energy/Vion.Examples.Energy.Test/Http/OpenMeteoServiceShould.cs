using System;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Http.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.Energy.Services;
using Xunit;

namespace Vion.Examples.Energy.Test.Http
{
    /// <summary>
    ///     The forecast lookup against Open-Meteo, with the answers scripted through the HTTP test kit. The service reads the
    ///     wall clock to interpolate and to expire its cache, so the scripted hours bracket the current hour with one value,
    ///     which interpolates to that value whatever the time.
    /// </summary>
    public sealed class OpenMeteoServiceShould : IDisposable
    {
        private readonly ServiceHostBlock _block = new(LogicBlockTestHelper.CreateLoggerMock().Object);

        private readonly LogicBlockTestContext<ServiceHostBlock> _context;

        private readonly FakeHttpHarness _harness = new();

        private readonly OpenMeteoService _sut;

        public OpenMeteoServiceShould()
        {
            _context = _block.CreateTestContext().Build();
            _sut = new OpenMeteoService(_harness.Client, NullLogger<OpenMeteoService>.Instance);
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        public void RequestHourlyVariablesForLocationInInvariantCulture(string culture)
        {
            // Arrange — de-DE writes a decimal comma, which the API does not read as a coordinate
            var ambientCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            try
            {
                // Act
                _sut.Subscribe(_block, 47.4991723, 8.7291498, WeatherVariables.Temperature | WeatherVariables.ShortwaveRadiation, _ => { });

                // Assert
                Assert.Equal("https://api.open-meteo.com/v1/forecast?latitude=47.4992&longitude=8.7291&hourly=temperature_2m,shortwave_radiation&past_hours=2&forecast_hours=2",
                             Assert.Single(_harness.Requests).Uri.OriginalString);
            }
            finally
            {
                CultureInfo.CurrentCulture = ambientCulture;
            }
        }

        [Fact]
        public void DeliverValueInterpolatedFromAnswer()
        {
            // Arrange
            WeatherData? delivered = null;
            _sut.Subscribe(_block, 47.5, 8.7, WeatherVariables.Temperature, data => delivered = data);

            // Act
            _harness.Respond(Forecast(12.5));
            _context.FlushPendingActions();

            // Assert
            Assert.NotNull(delivered);
            Assert.Equal(12.5, delivered.Temperature);
            Assert.False(delivered.FromCache);
        }

        [Fact]
        public void ServeRepeatedSubscriptionFromCacheWithoutRequest()
        {
            // Arrange
            WeatherData? delivered = null;
            _sut.Subscribe(_block, 47.5, 8.7, WeatherVariables.Temperature, _ => { });
            _harness.Respond(Forecast(12.5));
            _context.FlushPendingActions();

            // Act
            _sut.Subscribe(_block, 47.5, 8.7, WeatherVariables.Temperature, data => delivered = data);

            // Assert
            Assert.Single(_harness.Requests);
            Assert.NotNull(delivered);
            Assert.True(delivered.FromCache);
            Assert.Equal(12.5, delivered.Temperature);
        }

        [Fact]
        public void ReportInvalidStructureWhenHourlyDataMissing()
        {
            // Arrange
            Exception? failure = null;
            _sut.Subscribe(_block, 47.5, 8.7, WeatherVariables.Temperature, _ => { }, error => failure = error);

            // Act
            _harness.Respond("{\"latitude\":47.5,\"longitude\":8.7}");
            _context.FlushPendingActions();

            // Assert
            Assert.Equal("Invalid weather data structure", Assert.IsType<InvalidOperationException>(failure).Message);
        }

        private static string Forecast(double temperature)
        {
            var hour = DateTime.UtcNow.Date.AddHours(DateTime.UtcNow.Hour);
            var times = new[] { hour.AddHours(-1), hour.AddHours(2) }.Select(time => "\"" + time.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) + "\"");
            var value = temperature.ToString(CultureInfo.InvariantCulture);

            return "{\"hourly\":{\"time\":[" + string.Join(",", times) + "],\"temperature_2m\":[" + value + "," + value + "]}}";
        }
    }
}
