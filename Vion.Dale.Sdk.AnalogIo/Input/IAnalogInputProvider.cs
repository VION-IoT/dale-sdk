using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.AnalogIo.Input
{
    /// <summary>
    ///     The provider side of an analog input: a simulator binds it to drive the value a
    ///     <see cref="IAnalogInput" /> observes.
    /// </summary>
    /// <remarks>
    ///     Development and bench surface only. It stands in for the hardware an analog input would read, so a
    ///     configuration binding it is refused by the production runtime — bind it from simulator blocks, never
    ///     from a block meant to run on a device. Call <see cref="Drive" /> whenever the simulated signal
    ///     changes; the direction is one-way, so there is nothing to receive back.
    /// </remarks>
    [PublicApi]
    [ServiceProviderContractType("AnalogInputProvider", Consumers = LinkMultiplicity.ZeroOrOne, DevelopmentOnly = true)]
    public interface IAnalogInputProvider
    {
        /// <summary>Drives the value the analog input observes.</summary>
        /// <param name="value">The value the simulated signal now carries.</param>
        void Drive(double value);
    }
}