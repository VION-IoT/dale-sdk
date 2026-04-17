using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Services
{
    public interface IGeolocationService
    {
        /// <summary>
        ///     Gets the latitude and longitude for a given city name using a non-blocking callback approach.
        /// </summary>
        void GetCoordinates(LogicBlockBase context, string cityName, Action<(double Latitude, double Longitude)> callback, Action<Exception>? errorCallback = null);
    }
}