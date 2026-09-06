using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.TestKit
{
    /// <summary>
    ///     Extension methods to simulate digital output commands arriving at a provider in tests.
    /// </summary>
    [PublicApi]
    public static class IDigitalOutputProviderExtensions
    {
        /// <summary>
        ///     Raise the SetReceived event on an <see cref="IDigitalOutputProvider" /> for tests.
        /// </summary>
        /// <param name="digitalOutputProvider">The digital output provider instance to raise the event on.</param>
        /// <param name="value">The commanded digital output value.</param>
        public static void RaiseSetReceived(this IDigitalOutputProvider digitalOutputProvider, bool value)
        {
            if (digitalOutputProvider == null)
            {
                throw new ArgumentNullException(nameof(digitalOutputProvider));
            }

            if (digitalOutputProvider is not DigitalOutputProvider digitalOutputProviderImplementation)
            {
                throw new InvalidOperationException("Unable to raise SetReceived on provided IDigitalOutputProvider instance");
            }

            var logicBlockContractId = new LogicBlockContractId(Constants.LogicBlockId, digitalOutputProviderImplementation.Identifier);
            digitalOutputProviderImplementation.HandleContractMessage(new ContractMessage<SetDigitalOutput>(logicBlockContractId, new SetDigitalOutput(value)));
        }
    }
}