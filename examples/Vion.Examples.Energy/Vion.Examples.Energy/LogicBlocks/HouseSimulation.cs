using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.Utils;

namespace Vion.Examples.Energy.LogicBlocks
{
    [LogicBlockInfo("Haus Simulation", "home-3-line")]
    public class HouseSimulation : LogicBlockBase, IObservableElectricityConsumer
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly ILogger _logger;

        private DateTime? _lastUpdateTime;

        [ServiceProperty(Title = "Basisverbrauch", Unit = "kW")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public double BaseConsumption { get; set; } = 0.45;

        [ServiceProperty(Title = "Verbrauchsspitze Morgen", Unit = "kW")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public double MorningPeakConsumption { get; set; } = 1.2;

        [ServiceProperty(Title = "Verbrauchsspitze Kochen Abend", Unit = "kW")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public double EveningCookingPeakConsumption { get; set; } = 2;

        [ServiceProperty(Title = "Verbrauchsspitze Heizen Abend", Unit = "kW")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public double EventingHeatingPeakConsumption { get; set; } = 0.8;

        [ServiceProperty(Title = "Zeitzone")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public string TimeZone { get; set; } = "Europe/Zurich";

        [ServiceProperty(Title = "Wirkleistung", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung", Unit = "kW")]
        [Importance(Importance.Primary)]
        [Display(group: "Status")]
        public double ActivePowerConsuming { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Gesamtverbrauch Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Gesamtverbrauch Total", Unit = "kWh")]
        [Category(PropertyCategory.Metric)]
        [Importance(Importance.Secondary)]
        [Display(group: "Zähler")]
        public double EnergyConsumedTotal { get; private set; }

        public HouseSimulation(IDateTimeProvider dateTimeProvider, ILogger logger) : base(logger)
        {
            _dateTimeProvider = dateTimeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public ObservableElectricityConsumerContract.DataResponse HandleRequest(ObservableElectricityConsumerContract.DataRequest request)
        {
            return new ObservableElectricityConsumerContract.DataResponse(_dateTimeProvider.UtcNow, ActivePowerConsuming, EnergyConsumedTotal);
        }

        [Timer(5)]
        public void OnTimer()
        {
            var currentTime = _dateTimeProvider.UtcNow;
            if (_lastUpdateTime.HasValue)
            {
                var newActivePower = CalculatePower(currentTime);

                // Calculate energy as integral of power over time
                var energyIncrement = EnergyCalculator.CalculateEnergyIncrement(ActivePowerConsuming, newActivePower, _lastUpdateTime.Value, currentTime);

                EnergyConsumedTotal += energyIncrement;

                _logger.LogDebug("Energy increment: {EnergyInc:F6} kWh", energyIncrement);

                ActivePowerConsuming = newActivePower;

                _logger.LogInformation("Active power: {Power:F3} kW, Total energy: {Energy:F3} kWh", ActivePowerConsuming, EnergyConsumedTotal);
            }

            _lastUpdateTime = currentTime;

            this.SendStateUpdate(new ObservableElectricityConsumerContract.GridEffectStateUpdate(ActivePowerConsuming));
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }

        private double CalculatePower(DateTime utcTime)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);

            var hourOfDay = localTime.Hour + localTime.Minute / 60.0;

            // Parameters for a 4-person household with electric heating & cooking
            var pBase = BaseConsumption * 1000; // W
            var a1 = MorningPeakConsumption * 1000; // Morning peak
            var a2 = EveningCookingPeakConsumption * 1000; // Evening cooking peak
            var a3 = EventingHeatingPeakConsumption * 1000; // Late heating cycle

            const double sigma1 = 1.5;
            const double sigma2 = 2.0;
            const double sigma3 = 1.2;

            var morning = a1 * Gaussian(hourOfDay, 7.0, sigma1);
            var evening = a2 * Gaussian(hourOfDay, 18.0, sigma2);
            var heating = a3 * Gaussian(hourOfDay, 21.0, sigma3);

            var totalWatts = pBase + morning + evening + heating;

            return totalWatts / 1000; // Convert W to kW
        }

        private static double Gaussian(double time, double center, double sigma)
        {
            return Math.Exp(-Math.Pow(time - center, 2) / (2 * sigma * sigma));
        }
    }
}