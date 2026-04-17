using System;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.TestKit
{
    /// <summary>
    ///     Extension methods to simulate analog output changes in tests.
    /// </summary>
    [PublicApi]
    public static class IAnalogOutputExtensions
    {
        /// <summary>
        ///     Raise the OutputChanged event on an <see cref="IAnalogOutput" /> for tests.
        /// </summary>
        /// <param name="analogOutput">The analog output instance to raise the event on.</param>
        /// <param name="value">The new analog output value.</param>
        public static void RaiseOutputChanged(this IAnalogOutput analogOutput, double value)
        {
            if (analogOutput == null)
            {
                throw new ArgumentNullException(nameof(analogOutput));
            }

            if (analogOutput is not AnalogOutput analogOutputImplementation)
            {
                throw new InvalidOperationException("Unable to raise OutputChanged on provided IAnalogOutput instance");
            }

            var logicBlockContractId = new LogicBlockContractId("", analogOutputImplementation.Identifier);
            analogOutputImplementation.HandleContractMessage(new ContractMessage<AnalogOutputChanged>(logicBlockContractId, new AnalogOutputChanged(value)));
        }
    }
}