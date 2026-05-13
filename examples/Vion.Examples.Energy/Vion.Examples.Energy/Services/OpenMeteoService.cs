using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Http;

namespace Vion.Examples.Energy.Services
{
    public class OpenMeteoService : IMeteoService
    {
        // Cache fields - cache by location and variable combination
        private readonly Dictionary<string, (OpenMeteoResponse Data, DateTime Expiration)> _cache = new();

        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(15);

        private readonly ILogger<OpenMeteoService> _logger;

        private readonly ILogicBlockHttpClient _logicBlockHttpClient;

        // Active subscriptions
        private readonly List<MeteoSubscription> _subscriptions = [];

        public OpenMeteoService(ILogicBlockHttpClient logicBlockHttpClient, ILogger<OpenMeteoService> logger)
        {
            _logicBlockHttpClient = logicBlockHttpClient;
            _logger = logger;
        }

        public IMeteoSubscription Subscribe(LogicBlockBase context,
                                            double latitude,
                                            double longitude,
                                            WeatherVariables variables,
                                            Action<WeatherData> updateCallback,
                                            Action<Exception>? errorCallback = null)
        {
            var subscription = new MeteoSubscription(this,
                                                     context,
                                                     latitude,
                                                     longitude,
                                                     variables,
                                                     updateCallback,
                                                     errorCallback);
            _subscriptions.Add(subscription);

            // Trigger initial update
            subscription.RequestUpdate();

            return subscription;
        }

        private void GetWeatherData(LogicBlockBase context,
                                    double latitude,
                                    double longitude,
                                    WeatherVariables variables,
                                    DateTime currentTime,
                                    Action<WeatherData> callback,
                                    Action<Exception>? errorCallback)
        {
            var cacheKey = GetCacheKey(latitude, longitude, variables);

            // Check cache
            if (_cache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow < cached.Expiration)
            {
                _logger.LogDebug("Using cached weather data (expires: {ExpirationTime})", cached.Expiration);
                var data = BuildWeatherData(cached.Data,
                                            variables,
                                            latitude,
                                            longitude,
                                            currentTime,
                                            true,
                                            cached.Expiration);
                callback(data);
                return;
            }

            // Fetch new data
            var url = BuildApiUrl(latitude, longitude, variables);
            _logicBlockHttpClient.GetJson<OpenMeteoResponse>(context,
                                                             url,
                                                             response =>
                                                             {
                                                                 if (response?.Hourly == null)
                                                                 {
                                                                     _logger.LogError("Invalid weather data structure received");
                                                                     errorCallback?.Invoke(new InvalidOperationException("Invalid weather data structure"));
                                                                     return;
                                                                 }

                                                                 // Update cache
                                                                 var expiration = DateTime.UtcNow.Add(_cacheDuration);
                                                                 _cache[cacheKey] = (response, expiration);
                                                                 _logger.LogInformation("Weather data cached (expires: {ExpirationTime})", expiration);

                                                                 var data = BuildWeatherData(response,
                                                                                             variables,
                                                                                             latitude,
                                                                                             longitude,
                                                                                             currentTime,
                                                                                             false,
                                                                                             expiration);
                                                                 callback(data);
                                                             },
                                                             ex =>
                                                             {
                                                                 _logger.LogError(ex, "Failed to fetch weather data for location ({Lat}, {Lon})", latitude, longitude);
                                                                 errorCallback?.Invoke(ex);
                                                             });
        }

        private void RemoveSubscription(MeteoSubscription subscription)
        {
            _subscriptions.Remove(subscription);
        }

        private static string GetCacheKey(double latitude, double longitude, WeatherVariables variables)
        {
            return $"{latitude:F4}_{longitude:F4}_{(int)variables}";
        }

        private static string BuildApiUrl(double latitude, double longitude, WeatherVariables variables)
        {
            var sb = new StringBuilder($"https://api.open-meteo.com/v1/forecast?latitude={latitude:F4}&longitude={longitude:F4}&hourly=");

            var parameters = new List<string>();

            if (variables.HasFlag(WeatherVariables.Temperature))
            {
                parameters.Add("temperature_2m");
            }

            if (variables.HasFlag(WeatherVariables.RelativeHumidity))
            {
                parameters.Add("relative_humidity_2m");
            }

            if (variables.HasFlag(WeatherVariables.DewPoint))
            {
                parameters.Add("dew_point_2m");
            }

            if (variables.HasFlag(WeatherVariables.Precipitation))
            {
                parameters.Add("precipitation");
            }

            if (variables.HasFlag(WeatherVariables.Rain))
            {
                parameters.Add("rain");
            }

            if (variables.HasFlag(WeatherVariables.ShortwaveRadiation))
            {
                parameters.Add("shortwave_radiation");
            }

            if (variables.HasFlag(WeatherVariables.WindSpeed))
            {
                parameters.Add("wind_speed_10m");
            }

            if (variables.HasFlag(WeatherVariables.WindDirection))
            {
                parameters.Add("wind_direction_10m");
            }

            if (variables.HasFlag(WeatherVariables.CloudCover))
            {
                parameters.Add("cloud_cover");
            }

            if (variables.HasFlag(WeatherVariables.Pressure))
            {
                parameters.Add("pressure_msl");
            }

            sb.Append(string.Join(",", parameters));
            sb.Append("&past_hours=2&forecast_hours=2");

            return sb.ToString();
        }

        private WeatherData BuildWeatherData(OpenMeteoResponse response,
                                             WeatherVariables variables,
                                             double latitude,
                                             double longitude,
                                             DateTime currentTime,
                                             bool fromCache,
                                             DateTime cacheExpiration)
        {
            var data = new WeatherData
                       {
                           Latitude = latitude,
                           Longitude = longitude,
                           Timestamp = currentTime,
                           FromCache = fromCache,
                           CacheExpiration = cacheExpiration,
                       };
            if (response.Hourly?.Time == null)
            {
                return data;
            }

            if (variables.HasFlag(WeatherVariables.Temperature) && response.Hourly?.Temperature != null)
            {
                data.Temperature = InterpolateValue(response.Hourly.Time, response.Hourly.Temperature, currentTime, "Temperature");
            }

            if (variables.HasFlag(WeatherVariables.RelativeHumidity) && response.Hourly?.RelativeHumidity != null)
            {
                data.RelativeHumidity = InterpolateValue(response.Hourly.Time, response.Hourly.RelativeHumidity, currentTime, "Relative Humidity");
            }

            if (variables.HasFlag(WeatherVariables.DewPoint) && response.Hourly?.DewPoint != null)
            {
                data.DewPoint = InterpolateValue(response.Hourly.Time, response.Hourly.DewPoint, currentTime, "Dew Point");
            }

            if (variables.HasFlag(WeatherVariables.Precipitation) && response.Hourly?.Precipitation != null)
            {
                data.Precipitation = InterpolateValue(response.Hourly.Time, response.Hourly.Precipitation, currentTime, "Precipitation");
            }

            if (variables.HasFlag(WeatherVariables.Rain) && response.Hourly?.Rain != null)
            {
                data.Rain = InterpolateValue(response.Hourly.Time, response.Hourly.Rain, currentTime, "Rain");
            }

            if (variables.HasFlag(WeatherVariables.ShortwaveRadiation) && response.Hourly?.ShortwaveRadiation != null)
            {
                data.ShortwaveRadiation = InterpolateValue(response.Hourly.Time, response.Hourly.ShortwaveRadiation, currentTime, "Shortwave Radiation");
            }

            if (variables.HasFlag(WeatherVariables.WindSpeed) && response.Hourly?.WindSpeed != null)
            {
                data.WindSpeed = InterpolateValue(response.Hourly.Time, response.Hourly.WindSpeed, currentTime, "Wind Speed");
            }

            if (variables.HasFlag(WeatherVariables.WindDirection) && response.Hourly?.WindDirection != null)
            {
                data.WindDirection = InterpolateValue(response.Hourly.Time, response.Hourly.WindDirection, currentTime, "Wind Direction");
            }

            if (variables.HasFlag(WeatherVariables.CloudCover) && response.Hourly?.CloudCover != null)
            {
                data.CloudCover = InterpolateValue(response.Hourly.Time, response.Hourly.CloudCover, currentTime, "Cloud Cover");
            }

            if (variables.HasFlag(WeatherVariables.Pressure) && response.Hourly?.Pressure != null)
            {
                data.Pressure = InterpolateValue(response.Hourly.Time, response.Hourly.Pressure, currentTime, "Pressure");
            }

            return data;
        }

        private double? InterpolateValue(string[] times, double[] values, DateTime targetTime, string valueName)
        {
            if (times == null || values == null || times.Length != values.Length || times.Length < 2)
            {
                _logger.LogError("Invalid weather data: mismatched or insufficient arrays for {ValueName}", valueName);
                return null;
            }

            var points = times.Select((time, i) => (Time: DateTime.Parse(time), Value: values[i])).OrderBy(p => p.Time).ToArray();

            for (var i = 0; i < points.Length - 1; i++)
            {
                var (t1, v1) = points[i];
                var (t2, v2) = points[i + 1];

                if (targetTime >= t1 && targetTime <= t2)
                {
                    var ratio = (targetTime - t1).TotalSeconds / (t2 - t1).TotalSeconds;
                    var interpolated = v1 + (v2 - v1) * ratio;
                    _logger.LogDebug("Interpolated {ValueName}: {Result:F2} (between {T1} and {T2})", valueName, interpolated, t1, t2);
                    return interpolated;
                }
            }

            if (targetTime < points[0].Time)
            {
                _logger.LogWarning("Target time before data range for {ValueName}, using first value: {Value}", valueName, points[0].Value);
                return points[0].Value;
            }

            if (targetTime > points[^1].Time)
            {
                _logger.LogWarning("Target time after data range for {ValueName}, using last value: {Value}", valueName, points[^1].Value);
                return points[^1].Value;
            }

            return null;
        }

        private class MeteoSubscription : IMeteoSubscription
        {
            private readonly LogicBlockBase _context;

            private readonly Action<Exception>? _errorCallback;

            private readonly double _latitude;

            private readonly double _longitude;

            private readonly OpenMeteoService _service;

            private readonly Action<WeatherData> _updateCallback;

            private readonly WeatherVariables _variables;

            private bool _disposed;

            public MeteoSubscription(OpenMeteoService service,
                                     LogicBlockBase context,
                                     double latitude,
                                     double longitude,
                                     WeatherVariables variables,
                                     Action<WeatherData> updateCallback,
                                     Action<Exception>? errorCallback)
            {
                _service = service;
                _context = context;
                _latitude = latitude;
                _longitude = longitude;
                _variables = variables;
                _updateCallback = updateCallback;
                _errorCallback = errorCallback;
            }

            public void RequestUpdate()
            {
                if (_disposed)
                {
                    return;
                }

                _service.GetWeatherData(_context,
                                        _latitude,
                                        _longitude,
                                        _variables,
                                        DateTime.UtcNow,
                                        _updateCallback,
                                        _errorCallback);
            }

            public void Unsubscribe()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _service.RemoveSubscription(this);
            }

            public void Dispose()
            {
                Unsubscribe();
            }
        }
    }

    public class OpenMeteoResponse
    {
        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }

        [JsonPropertyName("generationtime_ms")]
        public double GenerationTimeMs { get; set; }

        [JsonPropertyName("utc_offset_seconds")]
        public int UtcOffsetSeconds { get; set; }

        [JsonPropertyName("timezone")]
        public string? Timezone { get; set; }

        [JsonPropertyName("timezone_abbreviation")]
        public string? TimezoneAbbreviation { get; set; }

        [JsonPropertyName("elevation")]
        public double? Elevation { get; set; }

        [JsonPropertyName("hourly_units")]
        public HourlyUnits? HourlyUnits { get; set; }

        [JsonPropertyName("hourly")]
        public HourlyData? Hourly { get; set; }
    }

    public class HourlyUnits
    {
        [JsonPropertyName("time")]
        public string? Time { get; set; }

        [JsonPropertyName("temperature_2m")]
        public string? Temperature { get; set; }

        [JsonPropertyName("relative_humidity_2m")]
        public string? RelativeHumidity { get; set; }

        [JsonPropertyName("dew_point_2m")]
        public string? DewPoint { get; set; }

        [JsonPropertyName("precipitation")]
        public string? Precipitation { get; set; }

        [JsonPropertyName("rain")]
        public string? Rain { get; set; }

        [JsonPropertyName("shortwave_radiation")]
        public string? ShortwaveRadiation { get; set; }

        [JsonPropertyName("wind_speed_10m")]
        public string? WindSpeed { get; set; }

        [JsonPropertyName("wind_direction_10m")]
        public string? WindDirection { get; set; }

        [JsonPropertyName("cloud_cover")]
        public string? CloudCover { get; set; }

        [JsonPropertyName("pressure_msl")]
        public string? Pressure { get; set; }
    }

    public class HourlyData
    {
        [JsonPropertyName("time")]
        public string[]? Time { get; set; }

        [JsonPropertyName("temperature_2m")]
        public double[]? Temperature { get; set; }

        [JsonPropertyName("relative_humidity_2m")]
        public double[]? RelativeHumidity { get; set; }

        [JsonPropertyName("dew_point_2m")]
        public double[]? DewPoint { get; set; }

        [JsonPropertyName("precipitation")]
        public double[]? Precipitation { get; set; }

        [JsonPropertyName("rain")]
        public double[]? Rain { get; set; }

        [JsonPropertyName("shortwave_radiation")]
        public double[]? ShortwaveRadiation { get; set; }

        [JsonPropertyName("wind_speed_10m")]
        public double[]? WindSpeed { get; set; }

        [JsonPropertyName("wind_direction_10m")]
        public double[]? WindDirection { get; set; }

        [JsonPropertyName("cloud_cover")]
        public double[]? CloudCover { get; set; }

        [JsonPropertyName("pressure_msl")]
        public double[]? Pressure { get; set; }
    }
}