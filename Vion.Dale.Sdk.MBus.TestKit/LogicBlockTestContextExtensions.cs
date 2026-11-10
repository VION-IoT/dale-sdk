using Moq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.MBus.TestKit
{
    /// <summary>
    ///     Verification extension methods for asserting M-Bus reads in tests.
    /// </summary>
    [PublicApi]
    public static class LogicBlockTestContextExtensions
    {
        /// <summary>
        ///     Asserts that an M-Bus read was sent.
        /// </summary>
        /// <param name="testContext">The test context for the logic block.</param>
        /// <param name="mBus">The M-Bus contract to filter by, or null for any.</param>
        /// <param name="primaryAddress">The expected primary address, or null to skip verification.</param>
        /// <param name="maxTelegrams">The expected telegram count, or null to skip verification.</param>
        /// <param name="times">The expected number of times, or null for once.</param>
        public static void VerifyMBusReadSent<T>(this LogicBlockTestContext<T> testContext, IMBus? mBus = null, int? primaryAddress = null, int? maxTelegrams = null, Times? times = null)
            where T : LogicBlockBase
        {
            string? identifier = null;
            if (mBus != null)
            {
                if (mBus is not MBusContract contract)
                {
                    throw new TestKitVerificationException("Unable to assert an M-Bus read on this IMBus instance: it is not the SDK's M-Bus contract.");
                }

                identifier = contract.Identifier;
            }

            testContext.VerifyContractMessageSent<ReadMBusRequest>("MBusRead",
                                                                   identifier,
                                                                   request => (primaryAddress == null || request.PrimaryAddress == primaryAddress.Value) &&
                                                                              (maxTelegrams == null || request.MaxTelegrams == maxTelegrams.Value),
                                                                   times);
        }
    }
}
