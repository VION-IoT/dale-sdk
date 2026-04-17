using System;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.AnalogIo.Output
{
    /// <summary>
    ///     Represents an analog output that can be used to communicate with hardware.
    /// </summary>
    [PublicApi]
    [ServiceProviderContractType("AnalogOutput")]
    public interface IAnalogOutput
    {
        /// <summary>
        ///     Occurs when the analog output state changes.
        /// </summary>
        event EventHandler<double>? OutputChanged;

        /// <summary>
        ///     Sets the analog output to the specified value.
        /// </summary>
        /// <param name="value">The value to set the analog output to.</param>
        void Set(double value);
    }
}