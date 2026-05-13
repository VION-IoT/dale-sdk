using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Contracts
{
    [LogicBlockContract(BetweenInterface = "IWeatherDataProvider",
              AndInterface = "IWeatherDataConsumer",
              BetweenDefaultName = "Wetterdatenquelle",
              AndDefaultName = "Wetterdatenempfänger",
              Direction = ContractDirection.None)]
    public static class WeatherDataContract
    {
        [StateUpdate(From = "IWeatherDataProvider", To = "IWeatherDataConsumer")]
        public readonly record struct WeatherData(
            double Temperature,
            double RelativeHumidity,
            double DewPoint,
            double Precipitation,
            double Rain,
            double ShortwaveRadiation,
            double WindSpeed,
            double WindDirection,
            double CloudCover,
            double Pressure);
    }
}