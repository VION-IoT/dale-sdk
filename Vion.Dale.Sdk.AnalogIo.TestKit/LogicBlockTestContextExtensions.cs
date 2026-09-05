using System;
using System.Globalization;
using Moq;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.TestKit;

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
        /// <param name="tolerance">The inclusive tolerance for the value comparison; the default of 0 means exact equality.</param>
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

            EnsureUsableTolerance(tolerance);
            testContext.VerifyContractMessageSent<SetAnalogOutput>("AnalogOutput", identifier, m => Matches(m.Value, value, tolerance), times);
        }

        /// <summary>
        ///     Assert that the specified analog output provider confirmed the given value.
        /// </summary>
        /// <typeparam name="TLogicBlock">The type of logic block being tested.</typeparam>
        /// <param name="testContext">The test context for the logic block.</param>
        /// <param name="analogOutputProvider">The analog output provider to verify, or null to verify any.</param>
        /// <param name="value">The expected value, or null to skip value verification.</param>
        /// <param name="tolerance">The inclusive tolerance for the value comparison; the default of 0 means exact equality.</param>
        /// <param name="times">The expected number of confirmations, or null for once.</param>
        public static void VerifyAnalogOutputConfirmed<TLogicBlock>(this LogicBlockTestContext<TLogicBlock> testContext,
                                                                    IAnalogOutputProvider? analogOutputProvider = null,
                                                                    double? value = null,
                                                                    double tolerance = 0,
                                                                    Times? times = null)
            where TLogicBlock : LogicBlockBase
        {
            string? identifier = null;
            if (analogOutputProvider != null)
            {
                if (analogOutputProvider is not AnalogOutputProvider analogOutputProviderImplementation)
                {
                    throw new TestKitVerificationException("Unable to assert analog output provider confirmation");
                }

                identifier = analogOutputProviderImplementation.Identifier;
            }

            EnsureUsableTolerance(tolerance);
            testContext.VerifyContractMessageSent<AnalogOutputChanged>("AnalogOutputProvider", identifier, m => Matches(m.Value, value, tolerance), times);
        }

        /// <summary>
        ///     Assert that the specified analog input provider drove the given value.
        /// </summary>
        /// <typeparam name="TLogicBlock">The type of logic block being tested.</typeparam>
        /// <param name="testContext">The test context for the logic block.</param>
        /// <param name="analogInputProvider">The analog input provider to verify, or null to verify any.</param>
        /// <param name="value">The expected value, or null to skip value verification.</param>
        /// <param name="tolerance">The inclusive tolerance for the value comparison; the default of 0 means exact equality.</param>
        /// <param name="times">The expected number of drives, or null for once.</param>
        public static void VerifyAnalogInputDriven<TLogicBlock>(this LogicBlockTestContext<TLogicBlock> testContext,
                                                                IAnalogInputProvider? analogInputProvider = null,
                                                                double? value = null,
                                                                double tolerance = 0,
                                                                Times? times = null)
            where TLogicBlock : LogicBlockBase
        {
            string? identifier = null;
            if (analogInputProvider != null)
            {
                if (analogInputProvider is not AnalogInputProvider analogInputProviderImplementation)
                {
                    throw new TestKitVerificationException("Unable to assert analog input provider drive");
                }

                identifier = analogInputProviderImplementation.Identifier;
            }

            EnsureUsableTolerance(tolerance);
            testContext.VerifyContractMessageSent<AnalogInputChanged>("AnalogInputProvider", identifier, m => Matches(m.Value, value, tolerance), times);
        }

        /// <summary>
        ///     Bit equality first, then the tolerance band. The difference comparison alone is false for a
        ///     non-number against itself and for either infinity against itself at every tolerance, and the
        ///     value contract admits all three unaltered in both directions — so a block that legitimately
        ///     writes one has to be assertable. A signed zero matches an unsigned one either way.
        /// </summary>
        private static bool Matches(double actual, double? expected, double tolerance)
        {
            return expected == null || actual.Equals(expected.Value) || Math.Abs(actual - expected.Value) <= tolerance;
        }

        /// <summary>
        ///     A tolerance is a width, so it is a number of at least zero. A non-number or a negative one
        ///     makes the band empty and rejects even an exact value, which no caller can mean; an infinite
        ///     tolerance is a legal width that admits every finite value.
        /// </summary>
        private static void EnsureUsableTolerance(double tolerance)
        {
            if (double.IsNaN(tolerance) || tolerance < 0)
            {
                throw new TestKitVerificationException(string.Format(CultureInfo.InvariantCulture,
                                                                     "A verification tolerance must be a number of at least zero, but was {0}.",
                                                                     tolerance));
            }
        }
    }
}