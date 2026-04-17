using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Services
{
    public interface IMeteoService
    {
        /// <summary>
        ///     Subscribe to weather data updates for specified variables
        /// </summary>
        IMeteoSubscription Subscribe(LogicBlockBase context,
                                     double latitude,
                                     double longitude,
                                     WeatherVariables variables,
                                     Action<WeatherData> updateCallback,
                                     Action<Exception>? errorCallback = null);
    }

    public interface IMeteoSubscription : IDisposable
    {
        void RequestUpdate();

        void Unsubscribe();
    }

    [Flags]
    public enum WeatherVariables
    {
        None = 0,

        Temperature = 1 << 0, // temperature_2m (°C)

        RelativeHumidity = 1 << 1, // relative_humidity_2m (%)

        DewPoint = 1 << 2, // dew_point_2m (°C)

        Precipitation = 1 << 3, // precipitation (mm)

        Rain = 1 << 4, // rain (mm)

        ShortwaveRadiation = 1 << 5, // shortwave_radiation (W/m²)

        WindSpeed = 1 << 6, // wind_speed_10m (km/h)

        WindDirection = 1 << 7, // wind_direction_10m (°)

        CloudCover = 1 << 8, // cloud_cover (%)

        Pressure = 1 << 9, // pressure_msl (hPa)

        // Common combinations
        Basic = Temperature | RelativeHumidity | Precipitation,

        Solar = ShortwaveRadiation | CloudCover,

        Wind = WindSpeed | WindDirection,

        All = Temperature | RelativeHumidity | DewPoint | Precipitation | Rain | ShortwaveRadiation | WindSpeed | WindDirection | CloudCover | Pressure,
    }

    public class WeatherData
    {
        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public DateTime Timestamp { get; set; }

        public bool FromCache { get; set; }

        public DateTime CacheExpiration { get; set; }

        // Weather variables (null if not requested)
        public double? Temperature { get; set; }

        public double? RelativeHumidity { get; set; }

        public double? DewPoint { get; set; }

        public double? Precipitation { get; set; }

        public double? Rain { get; set; }

        public double? ShortwaveRadiation { get; set; }

        public double? WindSpeed { get; set; }

        public double? WindDirection { get; set; }

        public double? CloudCover { get; set; }

        public double? Pressure { get; set; }
    }
}