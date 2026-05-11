using System;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.Services;
using Vion.Examples.Energy.Utils;

namespace Vion.Examples.Energy.LogicBlocks
{
    [LogicBlockInfo("Photovoltaik Simulation", "sun-line")]
    public class PhotovoltaicsSimulation : LogicBlockBase, IObservableElectricitySupplier
    {
        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly IGeolocationService _geolocationService;

        private readonly ILogger _logger;

        private readonly IMeteoService _meteoService;

        private DateTime? _lastUpdateTime;

        private string _locationName = "Winterthur, Switzerland";

        private IMeteoSubscription? _shortwaveRadiationSubscription;

        [ServiceProviderContract(defaultName: "Auslastung")]
        public IAnalogOutput ActivePowerPercentageOutput { get; private set; }

        [ServiceProperty(Title = "Fläche", Unit = "m²")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public double PanelArea { get; set; } = 100;

        [ServiceProperty(Title = "Wirkungsgrad", Unit = "Faktor")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public double PanelEfficiency { get; set; } = 0.2;

        [ServiceProperty(Title = "Maximale Kurzwellenstrahlung", Unit = "W/m²")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public double MaxShortwaveRadiation { get; set; } = 350;

        [ServiceProperty(Title = "Maximale Wirkleistung", Unit = "kW")]
        [Display(group: "Status")]
        public double PeakActivePower
        {
            get => PanelArea * PanelEfficiency * MaxShortwaveRadiation / 1000; // Convert W to kW
        }

        [ServiceProperty(Title = "Kurzwellenstrahlung", Unit = "W/m²")]
        [Display(group: "Status")]
        public double ShortwaveRadiation { get; private set; }

        [ServiceProperty(Title = "Ort")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Standort")]
        public string LocationName
        {
            get => _locationName;

            set
            {
                if (_locationName != value) // on change
                {
                    _locationName = value;

                    // Update latitude and longitude based on the new location name
                    SetLocationByName(value);
                }
            }
        }

        [ServiceProperty(Title = "Geografische Breite", Unit = "deg")]
        [Persistent]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Standort")]
        public double Latitude { get; private set; } = 47.4991723; // Winterthur, Switzerland latitude

        [ServiceProperty(Title = "Geografishe Länge", Unit = "deg")]
        [Persistent]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Standort")]
        public double Longitude { get; private set; } = 8.7291498; // Winterthur, Switzerland longitude

        [ServiceProperty(Title = "Wirkleistung", Unit = "kW")]
        [ServiceMeasuringPoint(Title = "Wirkleistung", Unit = "kW")]
        [Importance(Importance.Primary)]
        [Display(group: "Status")]
        public double ActivePowerSupplying { get; private set; }

        [Persistent]
        [ServiceProperty(Title = "Zählerstand Gesamterzeugung Total", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Zählerstand Gesamterzeugung Total", Unit = "kWh")]
        [Category(PropertyCategory.Metric)]
        [Importance(Importance.Secondary)]
        [Display(group: "Zähler")]
        public double EnergySuppliedTotal { get; private set; }

        public PhotovoltaicsSimulation(IDateTimeProvider dateTimeProvider, IMeteoService meteoService, IGeolocationService geolocationService, ILogger logger) :
            base(logger)
        {
            _dateTimeProvider = dateTimeProvider;
            _meteoService = meteoService;
            _geolocationService = geolocationService;
            _logger = logger;
        }

        /// <inheritdoc />
        public ObservableElectricitySupplierContract.DataResponse HandleRequest(ObservableElectricitySupplierContract.DataRequest request)
        {
            return new ObservableElectricitySupplierContract.DataResponse(_dateTimeProvider.UtcNow, ActivePowerSupplying, EnergySuppliedTotal);
        }

        [Timer(5)]
        public void OnTimer()
        {
            var currentTime = _dateTimeProvider.UtcNow;
            if (_lastUpdateTime.HasValue)
            {
                _shortwaveRadiationSubscription?.RequestUpdate();

                // Calculate actual power: Area (m²) * Shortwave Radiation (W/m²) * Efficiency
                var newActivePower = PanelArea * ShortwaveRadiation * PanelEfficiency / 1000; // Convert W to kW

                // Calculate energy as integral of power over time
                var energyIncrement = EnergyCalculator.CalculateEnergyIncrement(ActivePowerSupplying, newActivePower, _lastUpdateTime.Value, currentTime);

                EnergySuppliedTotal += energyIncrement;
                ActivePowerSupplying = newActivePower;
                ActivePowerPercentageOutput.Set(Math.Clamp(PeakActivePower > 0 ? ActivePowerSupplying / PeakActivePower * 100 : 0, 0, 100));

                _logger.LogDebug("Energy increment: {EnergyInc:F6} kWh", energyIncrement);

                _logger.LogInformation("Solar radiation: {Radiation} W/m², Active power: {Power:F3} kW, Total energy: {Energy:F3} kWh",
                                       ShortwaveRadiation,
                                       ActivePowerSupplying,
                                       EnergySuppliedTotal);
            }

            _lastUpdateTime = currentTime;

            this.SendStateUpdate(new ObservableElectricitySupplierContract.GridEffectStateUpdate(ActivePowerSupplying * -1));
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            _shortwaveRadiationSubscription = _meteoService.Subscribe(this,
                                                                      Latitude,
                                                                      Longitude,
                                                                      WeatherVariables.ShortwaveRadiation,
                                                                      data =>
                                                                      {
                                                                          ShortwaveRadiation = data.ShortwaveRadiation ?? 0;
                                                                          _logger.LogInformation("Radiation updated: {Radiation} W/m² (from cache: {FromCache})",
                                                                                                 data.ShortwaveRadiation,
                                                                                                 data.FromCache);
                                                                      },
                                                                      error => { _logger.LogError(error, "Failed to get radiation data"); });
        }

        protected override void Starting()
        {
            this.SendStateUpdate(new ObservableElectricitySupplierContract.StateUpdate(PeakActivePower));
        }

        private void SetLocationByName(string cityName)
        {
            _geolocationService.GetCoordinates(this,
                                               cityName,
                                               coordinates =>
                                               {
                                                   Latitude = coordinates.Latitude;
                                                   Longitude = coordinates.Longitude;

                                                   _logger.LogInformation("Location updated to '{CityName}': Lat={Latitude:F6}, Lon={Longitude:F6}", cityName, Latitude, Longitude);
                                               },
                                               error => { _logger.LogError(error, "Error getting coordinates for city '{CityName}'", cityName); });
        }
    }
}