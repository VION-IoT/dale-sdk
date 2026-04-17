using System;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.DigitalIo.Input
{
    /// <summary>
    ///     Represents a digital input that can be used to communicate with hardware.
    /// </summary>
    [PublicApi]
    [ServiceProviderContractType("DigitalInput")]
    public interface IDigitalInput
    {
        /// <summary>
        ///     Occurs when the digital input state changes.
        /// </summary>
        event EventHandler<bool>? InputChanged;
    }
}