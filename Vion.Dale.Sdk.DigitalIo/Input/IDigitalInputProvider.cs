using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.DigitalIo.Input
{
    /// <summary>
    ///     The provider side of a digital input: a simulator binds it to drive the value a
    ///     <see cref="IDigitalInput" /> observes.
    /// </summary>
    /// <remarks>
    ///     Development and bench surface only. It stands in for the hardware a digital input would read, so a
    ///     configuration binding it is refused by the production runtime — bind it from simulator blocks, never
    ///     from a block meant to run on a device. Call <see cref="Drive" /> whenever the simulated signal
    ///     changes; the direction is one-way, so there is nothing to receive back.
    /// </remarks>
    [PublicApi]
    [ServiceProviderContractType("DigitalInputProvider", Consumers = LinkMultiplicity.ZeroOrOne, DevelopmentOnly = true)]
    public interface IDigitalInputProvider
    {
        /// <summary>Drives the value the digital input observes.</summary>
        /// <param name="value">The value the simulated signal now carries.</param>
        void Drive(bool value);
    }
}