using System;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.TestKit;
using Moq;

namespace Vion.Dale.Sdk.AnalogIo.TestKit
{
    /// <summary>
    ///     Extension methods to verify analog output messages in test contexts.
    /// </summary>
    [PublicApi]
    public static class LogicBlockTestContextExtensions
    {
        /// <summary>
        ///     Assert that at the specified analog output was set with the given value.
        /// </summary>
        /// <typeparam name="TLogicBlock">The type of logic block being tested.</typeparam>
        /// <param name="testContext">The test context for the logic block.</param>
        /// <param name="analogOutput">The analog output to verify, or null to verify any analog output.</param>
        /// <param name="value">The expected value, or null to skip value verification.</param>
        /// <param name="tolerance">The tolerance for comparing the expected value.</param>
        /// <param name="times">The expected number of times the output was set, or null for once.</param>
        public static void VerifyAnalogOutputSet<TLogicBlock>(this LogicBlockTestContext<TLogicBlock> testContext,
                                                              IAnalogOutput? analogOutput = null,
                                                              double? value = null,
                                                              double tolerance = 0,
                                                              Times? times = null)
            where TLogicBlock : LogicBlockBase
        {
            string? identifier = null;
            if (analogOutput != null)
            {
                if (analogOutput is not AnalogOutput analogOutputImplementation)
                {
                    throw new TestKitVerificationException("Unable to assert analog output state");
                }

                identifier = analogOutputImplementation.Identifier;
            }

            testContext.VerifyContractMessageSent<SetAnalogOutput>("AnalogOutput", identifier, m => value == null || Math.Abs(m.Value - value.Value) < tolerance, times);
        }
    }
}