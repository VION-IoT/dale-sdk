using System;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.AnalogIo.Input
{
    /// <summary>
    ///     Represents an analog input that can be used to communicate with hardware.
    /// </summary>
    [PublicApi]
    [ServiceProviderContractType("AnalogInput")]
    public interface IAnalogInput
    {
        /// <summary>
        ///     Occurs when the analog input state changes.
        /// </summary>
        event EventHandler<double>? InputChanged;
    }
}