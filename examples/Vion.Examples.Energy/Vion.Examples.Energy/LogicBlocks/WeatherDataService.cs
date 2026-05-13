using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;
using Vion.Examples.Energy.Contracts;
using Vion.Examples.Energy.Services;

namespace Vion.Examples.Energy.LogicBlocks
{
    [LogicBlock(Name = "Wetterdaten", Icon = "cloud-line")]
    public class WeatherDataService : LogicBlockBase, IWeatherDataProvider
    {
        private readonly IGeolocationService _geolocationService;

        private readonly ILogger _logger;

        private readonly IMeteoService _meteoService;

        private string _locationName = "Winterthur, Switzerland";

        private IMeteoSubscription? _meteoSubscription;

        [ServiceProperty(Title = "Temperatur", Unit = "°C")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public double Temperature { get; private set; }

        [ServiceProperty(Title = "Luftfeuchtigkeit", Unit = "%")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public double RelativeHumidity { get; private set; }

        [ServiceProperty(Title = "Taupunkt", Unit = "°C")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public double DewPoint { get; private set; }

        [ServiceProperty(Title = "Niederschlag", Unit = "mm")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public double Precipitation { get; private set; }

        [ServiceProperty(Title = "Regen", Unit = "mm")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public double Rain { get; private set; }

        [ServiceProperty(Title = "Kurzwellenstrahlung", Unit = "W/m²")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public double ShortwaveRadiation { get; private set; }

        [ServiceProperty(Title = "Windgeschwindigkeit", Unit = "km/h")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public double WindSpeed { get; private set; }

        [ServiceProperty(Title = "Windrichtung", Unit = "°")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public double WindDirection { get; private set; }

        [ServiceProperty(Title = "Wolkenbedeckung", Unit = "%")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public double CloudCover { get; private set; }

        [ServiceProperty(Title = "Luftdruck", Unit = "hPa")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public double Pressure { get; private set; }

        [ServiceProperty(Title = "Ort")]
        [Presentation(Group = PropertyGroup.Identity)]
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
        [Presentation(Group = PropertyGroup.Identity)]
        public double Latitude { get; private set; } = 47.4991723; // Winterthur, Switzerland latitude

        [ServiceProperty(Title = "Geografische Länge", Unit = "deg")]
        [Persistent]
        [Presentation(Group = PropertyGroup.Identity)]
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