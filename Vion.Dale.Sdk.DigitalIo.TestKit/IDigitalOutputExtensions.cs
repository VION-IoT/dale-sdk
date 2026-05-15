using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.TestKit
{
    /// <summary>
    ///     Extension methods to simulate digital output changes in tests.
    /// </summary>
    [PublicApi]
    public static class IDigitalOutputExtensions
    {
        /// <summary>
        ///     Raise the OutputChanged event on an <see cref="IDigitalOutput" /> for tests.
        /// </summary>
        /// <param name="digitalOutput">The digital output instance to raise the event on.</param>
        /// <param name="value">The new digital output value.</param>
        public static void RaiseOutputChanged(this IDigitalOutput digitalOutput, bool value)
        {
            if (digitalOutput == null)
            {
                throw new ArgumentNullException(nameof(digitalOutput));
            }

            if (digitalOutput is not DigitalOutput digitalOutputImplementation)
            {
                throw new InvalidOperationException("Unable to raise OutputChanged on provided IDigitalOutput instance");
            }

            var logicBlockContractId = new LogicBlockContractId("", digitalOutputImplementation.Identifier);
            digitalOutputImplementation.HandleContractMessage(new ContractMessage<DigitalOutputChanged>(logicBlockContractId, new DigitalOutputChanged(value)));
        }
    }
}