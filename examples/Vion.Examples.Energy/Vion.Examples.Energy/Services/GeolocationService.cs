using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using System.Web;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Http;

namespace Vion.Examples.Energy.Services
{
    public class GeolocationService : IGeolocationService
    {
        private readonly ILogger<GeolocationService> _logger;

        private readonly ILogicBlockHttpClient _logicBlockHttpClient;

        public GeolocationService(ILogicBlockHttpClient logicBlockHttpClient, ILogger<GeolocationService> logger)
        {
            _logicBlockHttpClient = logicBlockHttpClient;
            _logger = logger;
        }

        /// <inheritdoc />
        public void GetCoordinates(LogicBlockBase context, string cityName, Action<(double Latitude, double Longitude)> callback, Action<Exception>? errorCallback = null)
        {
            if (string.IsNullOrWhiteSpace(cityName))
            {
                _logger.LogWarning("City name is null or empty");
                errorCallback?.Invoke(new ArgumentException("City name is null or empty"));
                return;
            }

            var encodedCityName = HttpUtility.UrlEncode(cityName);
            var url = $"https://nominatim.openstreetmap.org/search?format=json&q={encodedCityName}";
            _logicBlockHttpClient.GetJson<NominatimResponse[]>(context,
                                                               url,
                                                               response =>
                                                               {
                                                                   if (response == null || response.Length == 0)
                                                                   {
                                                                       _logger.LogWarning("No geolocation results found for '{CityName}'", cityName);
                                                                       errorCallback?.Invoke(new ArgumentException("No geolocation results found"));
                                                                       return;
                                                                   }

                                                                   // Take the first result (usually the most relevant)
                                                                   var firstResult = response.First();

                                                                   if (!double.TryParse(firstResult.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                                                                       !double.TryParse(firstResult.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
                                                                   {
                                                                       _logger.LogError("Failed to parse coordinates for '{CityName}'. Lat: {Lat}, Lon: {Lon}",
                                                                                        cityName,
                                                                                        firstResult.Lat,
                                                                                        firstResult.Lon);
                                                                       errorCallback?.Invoke(new ArgumentException("Failed to parse coordinates"));
                                                                       return;
                                                                   }

                                                                   _logger
                                                                       .LogInformation("Found coordinates for '{CityName}': Lat={Latitude:F6}, Lon={Longitude:F6} (Display: {DisplayName})",
                                                                                       cityName,
                                                                                       latitude,
                                                                                       longitude,
                                                                                       firstResult.DisplayName);

                                                                   callback((latitude, longitude));
                                                               },
                                                               error =>
                                                               {
                                                                   _logger.LogError(error, "Failed to fetch geolocation data for '{CityName}'", cityName);
                                                                   errorCallback?.Invoke(error);
                                                               });
        }
    }

    public class NominatimResponse
    {
        [JsonPropertyName("place_id")]
        public long PlaceId { get; set; }

        [JsonPropertyName("licence")]
        public string? Licence { get; set; }

        [JsonPropertyName("osm_type")]
        public string? OsmType { get; set; }

        [JsonPropertyName("osm_id")]
        public long OsmId { get; set; }

        [JsonPropertyName("lat")]
        public string? Lat { get; set; }

        [JsonPropertyName("lon")]
        public string? Lon { get; set; }

        [JsonPropertyName("class")]
        public string? Class { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("place_rank")]
        public int PlaceRank { get; set; }

        [JsonPropertyName("importance")]
        public double Importance { get; set; }

        [JsonPropertyName("addresstype")]
        public string? AddressType { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("boundingbox")]
        public string[]? BoundingBox { get; set; }
    }
}