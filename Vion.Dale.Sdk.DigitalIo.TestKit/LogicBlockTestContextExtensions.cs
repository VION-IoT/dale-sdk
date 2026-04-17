using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.TestKit;
using Moq;

namespace Vion.Dale.Sdk.DigitalIo.TestKit
{
    /// <summary>
    ///     Extension methods to verify digital output messages in test contexts.
    /// </summary>
    [PublicApi]
    public static class LogicBlockTestContextExtensions
    {
        /// <summary>
        ///     Assert that at the specified digital output was set with the given value.
        /// </summary>
        /// <typeparam name="TLogicBlock">The type of logic block being tested.</typeparam>
        /// <param name="testContext">The test context for the logic block.</param>
        /// <param name="digitalOutput">The digital output to verify, or null to verify any digital output.</param>
        /// <param name="value">The expected value, or null to skip value verification.</param>
        /// <param name="times">The expected number of times the output was set, or null for once.</param>
        public static void VerifyDigitalOutputSet<TLogicBlock>(this LogicBlockTestContext<TLogicBlock> testContext,
                                                               IDigitalOutput? digitalOutput = null,
                                                               bool? value = null,
                                                               Times? times = null)
            where TLogicBlock : LogicBlockBase
        {
            string? identifier = null;
            if (digitalOutput != null)
            {
                if (digitalOutput is not DigitalOutput digitalOutputImplementation)
                {
                    throw new TestKitVerificationException("Unable to assert digital output state");
                }

                identifier = digitalOutputImplementation.Identifier;
            }

            testContext.VerifyContractMessageSent<SetDigitalOutput>("DigitalOutput", identifier, m => value == null || m.Value == value.Value, times);
        }
    }
}