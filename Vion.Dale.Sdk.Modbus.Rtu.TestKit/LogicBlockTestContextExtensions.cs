using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.TestKit;
using Moq;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit
{
    /// <summary>
    ///     Verification extension methods for asserting Modbus RTU messages in tests.
    /// </summary>
    [PublicApi]
    public static class LogicBlockTestContextExtensions
    {
        /// <summary>
        ///     Assert that a Modbus read request was sent.
        /// </summary>
        /// <param name="testContext">The test context for the logic block.</param>
        /// <param name="modbusRtu">The Modbus RTU contract to filter by, or null for any.</param>
        /// <param name="startingAddress">The expected starting address, or null to skip verification.</param>
        /// <param name="quantity">The expected register/coil quantity, or null to skip verification.</param>
        /// <param name="times">The expected number of times, or null for once.</param>
        public static void VerifyModbusReadSent<T>(this LogicBlockTestContext<T> testContext,
                                                   IModbusRtu? modbusRtu = null,
                                                   ushort? startingAddress = null,
                                                   ushort? quantity = null,
                                                   Times? times = null)
            where T : LogicBlockBase
        {
            string? identifier = null;
            if (modbusRtu != null)
            {
                if (modbusRtu is not ModbusRtu impl)
                {
                    throw new TestKitVerificationException("Unable to assert Modbus read request");
                }

                identifier = impl.Identifier;
            }

            testContext.VerifyContractMessageSent<ReadModbusRtuRequest>("ModbusRead",
                                                                        identifier,
                                                                        m => (startingAddress == null || m.StartingAddress == startingAddress.Value) &&
                                                                             (quantity == null || m.Quantity == quantity.Value),
                                                                        times);
        }

        /// <summary>
        ///     Assert that a Modbus write request was sent.
        /// </summary>
        /// <param name="testContext">The test context for the logic block.</param>
        /// <param name="modbusRtu">The Modbus RTU contract to filter by, or null for any.</param>
        /// <param name="address">The expected write address, or null to skip verification.</param>
        /// <param name="times">The expected number of times, or null for once.</param>
        public static void VerifyModbusWriteSent<T>(this LogicBlockTestContext<T> testContext, IModbusRtu? modbusRtu = null, ushort? address = null, Times? times = null)
            where T : LogicBlockBase
        {
            string? identifier = null;
            if (modbusRtu != null)
            {
                if (modbusRtu is not ModbusRtu impl)
                {
                    throw new TestKitVerificationException("Unable to assert Modbus write request");
                }

                identifier = impl.Identifier;
            }

            testContext.VerifyContractMessageSent<WriteModbusRtuRequest>("ModbusWrite", identifier, m => address == null || m.Address == address.Value, times);
        }
    }
}