using System;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.DigitalIo.Output
{
    /// <summary>
    ///     Represents a digital output that can be used to communicate with hardware.
    /// </summary>
    [PublicApi]
    [ServiceProviderContractType("DigitalOutput", Consumers = LinkMultiplicity.ZeroOrOne)]
    public interface IDigitalOutput
    {
        /// <summary>
        ///     Occurs when the digital output state changes.
        /// </summary>
        event EventHandler<bool>? OutputChanged;

        /// <summary>
        ///     Sets the digital output to the specified value.
        /// </summary>
        /// <param name="value">The value to set the digital output to.</param>
        void Set(bool value);
    }
}