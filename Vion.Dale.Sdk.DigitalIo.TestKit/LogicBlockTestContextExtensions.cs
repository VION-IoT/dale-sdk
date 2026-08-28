using Moq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.TestKit;

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

        /// <summary>
        ///     Assert that the specified digital output provider confirmed the given value.
        /// </summary>
        /// <typeparam name="TLogicBlock">The type of logic block being tested.</typeparam>
        /// <param name="testContext">The test context for the logic block.</param>
        /// <param name="digitalOutputProvider">The digital output provider to verify, or null to verify any.</param>
        /// <param name="value">The expected value, or null to skip value verification.</param>
        /// <param name="times">The expected number of confirmations, or null for once.</param>
        public static void VerifyDigitalOutputConfirmed<TLogicBlock>(this LogicBlockTestContext<TLogicBlock> testContext,
                                                                     IDigitalOutputProvider? digitalOutputProvider = null,
                                                                     bool? value = null,
                                                                     Times? times = null)
            where TLogicBlock : LogicBlockBase
        {
            string? identifier = null;
            if (digitalOutputProvider != null)
            {
                if (digitalOutputProvider is not DigitalOutputProvider digitalOutputProviderImplementation)
                {
                    throw new TestKitVerificationException("Unable to assert digital output provider confirmation");
                }

                identifier = digitalOutputProviderImplementation.Identifier;
            }

            testContext.VerifyContractMessageSent<DigitalOutputChanged>("DigitalOutputProvider", identifier, m => value == null || m.Value == value.Value, times);
        }

        /// <summary>
        ///     Assert that the specified digital input provider drove the given value.
        /// </summary>
        /// <typeparam name="TLogicBlock">The type of logic block being tested.</typeparam>
        /// <param name="testContext">The test context for the logic block.</param>
        /// <param name="digitalInputProvider">The digital input provider to verify, or null to verify any.</param>
        /// <param name="value">The expected value, or null to skip value verification.</param>
        /// <param name="times">The expected number of drives, or null for once.</param>
        public static void VerifyDigitalInputDriven<TLogicBlock>(this LogicBlockTestContext<TLogicBlock> testContext,
                                                                 IDigitalInputProvider? digitalInputProvider = null,
                                                                 bool? value = null,
                                                                 Times? times = null)
            where TLogicBlock : LogicBlockBase
        {
            string? identifier = null;
            if (digitalInputProvider != null)
            {
                if (digitalInputProvider is not DigitalInputProvider digitalInputProviderImplementation)
                {
                    throw new TestKitVerificationException("Unable to assert digital input provider drive");
                }

                identifier = digitalInputProviderImplementation.Identifier;
            }

            testContext.VerifyContractMessageSent<DigitalInputChanged>("DigitalInputProvider", identifier, m => value == null || m.Value == value.Value, times);
        }
    }
}