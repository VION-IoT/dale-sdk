using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.Services;

namespace Vion.Examples.Energy.LogicBlocks
{
    [LogicBlockInfo("Wetterdaten", "cloud-line")]
    public class WeatherDataService : LogicBlockBase, IWeatherDataProvider
    {
        private readonly IGeolocationService _geolocationService;

        private readonly ILogger _logger;

        private readonly IMeteoService _meteoService;

        private string _locationName = "Winterthur, Switzerland";

        private IMeteoSubscription? _meteoSubscription;

        [ServiceProperty(Title = "Temperatur", Unit = "°C")]
        [ServiceMeasuringPoint(Title = "Temperatur", Unit = "°C")]
        [Importance(Importance.Primary)]
        [Display(group: "Wetter")]
        public double Temperature { get; private set; }

        [ServiceProperty(Title = "Luftfeuchtigkeit", Unit = "%")]
        [ServiceMeasuringPoint(Title = "Luftfeuchtigkeit", Unit = "%")]
        [Display(group: "Wetter")]
        public double RelativeHumidity { get; private set; }

        [ServiceProperty(Title = "Taupunkt", Unit = "°C")]
        [ServiceMeasuringPoint(Title = "Taupunkt", Unit = "°C")]
        [Display(group: "Wetter")]
        public double DewPoint { get; private set; }

        [ServiceProperty(Title = "Niederschlag", Unit = "mm")]
        [ServiceMeasuringPoint(Title = "Niederschlag", Unit = "mm")]
        [Display(group: "Wetter")]
        public double Precipitation { get; private set; }

        [ServiceProperty(Title = "Regen", Unit = "mm")]
        [ServiceMeasuringPoint(Title = "Regen", Unit = "mm")]
        [Display(group: "Wetter")]
        public double Rain { get; private set; }

        [ServiceProperty(Title = "Kurzwellenstrahlung", Unit = "W/m²")]
        [ServiceMeasuringPoint(Title = "Kurzwellenstrahlung", Unit = "W/m²")]
        [Importance(Importance.Secondary)]
        [Display(group: "Wetter")]
        public double ShortwaveRadiation { get; private set; }

        [ServiceProperty(Title = "Windgeschwindigkeit", Unit = "km/h")]
        [ServiceMeasuringPoint(Title = "Windgeschwindigkeit", Unit = "km/h")]
        [Display(group: "Wetter")]
        public double WindSpeed { get; private set; }

        [ServiceProperty(Title = "Windrichtung", Unit = "°")]
        [ServiceMeasuringPoint(Title = "Windrichtung", Unit = "°")]
        [Display(group: "Wetter")]
        public double WindDirection { get; private set; }

        [ServiceProperty(Title = "Wolkenbedeckung", Unit = "%")]
        [ServiceMeasuringPoint(Title = "Wolkenbedeckung", Unit = "%")]
        [Display(group: "Wetter")]
        public double CloudCover { get; private set; }

        [ServiceProperty(Title = "Luftdruck", Unit = "hPa")]
        [ServiceMeasuringPoint(Title = "Luftdruck", Unit = "hPa")]
        [Display(group: "Wetter")]
        public double Pressure { get; private set; }

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

        [ServiceProperty(Title = "Geografische Länge", Unit = "deg")]
        [Persistent]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Standort")]
        public double Longitude { get; private set; } = 8.7291498; // Winterthur, Switzerland longitude

        public WeatherDataService(IMeteoService meteoService, IGeolocationService geolocationService, ILogger logger) : base(logger)
        {
            _meteoService = meteoService;
            _geolocationService = geolocationService;
            _logger = logger;
        }

        [Timer(5)]
        public void OnTimer()
        {
            _meteoSubscription?.RequestUpdate();
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            SubscribeMeteoData();
        }

        private void SubscribeMeteoData()
        {
            _meteoSubscription?.Unsubscribe();
            _meteoSubscription = _meteoService.Subscribe(this,
                                                         Latitude,
                                                         Longitude,
                                                         WeatherVariables.All,
                                                         data =>
                                                         {
                                                             Temperature = data.Temperature ?? 0;
                                                             RelativeHumidity = data.RelativeHumidity ?? 0;
                                                             DewPoint = data.DewPoint ?? 0;
                                                             Precipitation = data.Precipitation ?? 0;
                                                             Rain = data.Rain ?? 0;
                                                             ShortwaveRadiation = data.ShortwaveRadiation ?? 0;
                                                             WindSpeed = data.WindSpeed ?? 0;
                                                             WindDirection = data.WindDirection ?? 0;
                                                             CloudCover = data.CloudCover ?? 0;
                                                             Pressure = data.Pressure ?? 0;
                                                             this.SendStateUpdate(new WeatherDataContract.WeatherData(Temperature,
                                                                                                                      RelativeHumidity,
                                                                                                                      DewPoint,
                                                                                                                      Precipitation,
                                                                                                                      Rain,
                                                                                                                      ShortwaveRadiation,
                                                                                                                      WindSpeed,
                                                                                                                      WindDirection,
                                                                                                                      CloudCover,
                                                                                                                      Pressure));
                                                         },
                                                         error => { _logger.LogError(error, "Failed to get weather data"); });
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

                                                   SubscribeMeteoData(); // resubscribe to meteo data with new coordinates
                                               },
                                               error => { _logger.LogError(error, "Error getting coordinates for city '{CityName}'", cityName); });
        }
    }
}