using System;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.DigitalIo.Output
{
    /// <summary>
    ///     The provider side of a digital output: a simulator binds it to receive what a
    ///     <see cref="IDigitalOutput" /> commanded and to confirm back the value it applied.
    /// </summary>
    /// <remarks>
    ///     Development and bench surface only. It stands in for the hardware a digital output would drive, so a
    ///     configuration binding it is refused by the production runtime — bind it from simulator blocks, never
    ///     from a block meant to run on a device. Handle <see cref="SetReceived" /> to model the equipment's
    ///     reaction, then call <see cref="Confirm" /> with the value it actually took; ignoring a command is a
    ///     legitimate model of hardware that did not take it up.
    /// </remarks>
    [PublicApi]
    [ServiceProviderContractType("DigitalOutputProvider", Consumers = LinkMultiplicity.ZeroOrOne, DevelopmentOnly = true)]
    public interface IDigitalOutputProvider
    {
        /// <summary>Occurs when a digital output commands a value.</summary>
        event EventHandler<bool>? SetReceived;

        /// <summary>Confirms the value the simulated hardware applied.</summary>
        /// <param name="value">The value that was applied.</param>
        void Confirm(bool value);
    }
}