using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.TestKit
{
    /// <summary>
    ///     Extension methods to simulate digital input changes in tests.
    /// </summary>
    [PublicApi]
    public static class IDigitalInputExtensions
    {
        /// <summary>
        ///     Raise the InputChanged event on an <see cref="IDigitalInput" /> for tests.
        /// </summary>
        /// <param name="digitalInput">The digital input instance to raise the event on.</param>
        /// <param name="value">The new digital input value.</param>
        public static void RaiseInputChanged(this IDigitalInput digitalInput, bool value)
        {
            if (digitalInput == null)
            {
                throw new ArgumentNullException(nameof(digitalInput));
            }

            if (digitalInput is not DigitalInput digitalInputImplementation)
            {
                throw new InvalidOperationException("Unable to raise InputChanged on provided IDigitalInput instance");
            }

            var logicBlockContractId = new LogicBlockContractId("", digitalInputImplementation.Identifier);
            digitalInputImplementation.HandleContractMessage(new ContractMessage<DigitalInputChanged>(logicBlockContractId, new DigitalInputChanged(value)));
        }
    }
}