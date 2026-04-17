using System;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.TestKit
{
    /// <summary>
    ///     Extension methods to simulate analog input changes in tests.
    /// </summary>
    [PublicApi]
    public static class IAnalogInputExtensions
    {
        /// <summary>
        ///     Raise the InputChanged event on an <see cref="IAnalogInput" /> for tests.
        /// </summary>
        /// <param name="analogInput">The analog input instance to raise the event on.</param>
        /// <param name="value">The new analog input value.</param>
        public static void RaiseInputChanged(this IAnalogInput analogInput, double value)
        {
            if (analogInput == null)
            {
                throw new ArgumentNullException(nameof(analogInput));
            }

            if (analogInput is not AnalogInput analogInputImplementation)
            {
                throw new InvalidOperationException("Unable to raise InputChanged on provided IAnalogInput instance");
            }

            var logicBlockContractId = new LogicBlockContractId("", analogInputImplementation.Identifier);
            analogInputImplementation.HandleContractMessage(new ContractMessage<AnalogInputChanged>(logicBlockContractId, new AnalogInputChanged(value)));
        }
    }
}