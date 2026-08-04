using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Energy.Contracts
{
    // Deliberately no [ServiceRelation]: relations are opt-in, and this contract is a data feed rather
    // than a link in the energy topology. Wiring it still works exactly as before — it just does not show
    // up as an edge in the block graph.
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