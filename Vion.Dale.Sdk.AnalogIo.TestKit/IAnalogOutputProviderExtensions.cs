using System;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.TestKit
{
    /// <summary>
    ///     Extension methods to simulate analog output commands arriving at a provider in tests.
    /// </summary>
    [PublicApi]
    public static class IAnalogOutputProviderExtensions
    {
        /// <summary>
        ///     Raise the SetReceived event on an <see cref="IAnalogOutputProvider" /> for tests.
        /// </summary>
        /// <param name="analogOutputProvider">The analog output provider instance to raise the event on.</param>
        /// <param name="value">The commanded analog output value.</param>
        public static void RaiseSetReceived(this IAnalogOutputProvider analogOutputProvider, double value)
        {
            if (analogOutputProvider == null)
            {
                throw new ArgumentNullException(nameof(analogOutputProvider));
            }

            if (analogOutputProvider is not AnalogOutputProvider analogOutputProviderImplementation)
            {
                throw new InvalidOperationException("Unable to raise SetReceived on provided IAnalogOutputProvider instance");
            }

            var logicBlockContractId = new LogicBlockContractId(Constants.LogicBlockId, analogOutputProviderImplementation.Identifier);
            analogOutputProviderImplementation.HandleContractMessage(new ContractMessage<SetAnalogOutput>(logicBlockContractId, new SetAnalogOutput(value)));
        }
    }
}