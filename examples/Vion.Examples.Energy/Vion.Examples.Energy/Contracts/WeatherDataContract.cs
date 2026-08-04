using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Contracts
{
    // Deliberately no [ServiceRelation]: relations are opt-in per contract, and this one is a data feed
    // rather than a link in the energy topology. Its endpoints bind and wire exactly like any other
    // contract's — they simply contribute no edge to the block graph.
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