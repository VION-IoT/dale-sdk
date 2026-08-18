using System;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Client.Request
{
    /// <summary>
    ///     The text of these two failures is a shipped surface, not a log line: a consumer publishes
    ///     <c>exception.Message</c> straight onto a service property, and a commissioning engineer reads it while
    ///     deciding whether the wiring is wrong. Each one has to say that the device was never contacted.
    /// </summary>
    [TestClass]
    public class RequestFailureMessagesShould
    {
        [TestMethod]
        public void SayTheQueueWasFullAndNameItsCapacityAndPolicy()
        {
            // Arrange / Act
            var exception = new RequestDroppedException("ReadHoldingRegistersRaw", 256, QueueOverflowPolicy.DropOldest);

            // Assert
            Assert.AreEqual("The 'ReadHoldingRegistersRaw' request was dropped before execution: the local request queue was full (capacity 256, policy DropOldest); " +
                            "the device was not contacted.",
                            exception.Message);
            Assert.AreEqual(RequestDropReason.QueueFull, exception.Reason);
            Assert.AreEqual("ReadHoldingRegistersRaw", exception.RequestName);
        }

        [TestMethod]
        public void SayTheClientWasDisposed()
        {
            // Arrange / Act
            var exception = new RequestDroppedException("ReadHoldingRegistersRaw");

            // Assert
            Assert.AreEqual("The 'ReadHoldingRegistersRaw' request was dropped before execution: the client was disposed.", exception.Message);
            Assert.AreEqual(RequestDropReason.ClientDisposed, exception.Reason);
        }

        [TestMethod]
        public void SayHowLongAnExpiredRequestWaitedAndAgainstWhichLimit()
        {
            // Arrange / Act
            var exception = new RequestExpiredException("ReadHoldingRegistersRaw", TimeSpan.FromMilliseconds(31_200), TimeSpan.FromSeconds(30));

            // Assert
            Assert.AreEqual("The 'ReadHoldingRegistersRaw' request expired in the local request queue after waiting 31.2 s (MaxQueuedAge 30 s); " + "the device was not contacted.",
                            exception.Message);
            Assert.AreEqual(TimeSpan.FromMilliseconds(31_200), exception.QueuedWait);
            Assert.AreEqual(TimeSpan.FromSeconds(30), exception.MaxQueuedAge);
        }
    }
}