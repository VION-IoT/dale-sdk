using System;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.TestKit.Test.TestHelpers;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     What the context records while a fixture block runs, and what the verification family reads back
    ///     off it. Time lives in <c>VirtualTimeShould</c> and the concurrency of the same recording in
    ///     <c>RecordingContextConcurrencyShould</c>; nothing here reaches a runtime, a broker, a device or
    ///     the development host.
    /// </summary>
    [TestClass]
    public class LogicBlockTestContextShould
    {
        private LogicBlockTestContext<SampleLogicBlock> _context = null!;

        private SampleLogicBlock _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = LogicBlockTestHelper.Create<SampleLogicBlock>();
            _context = _sut.CreateTestContext().Build();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-004.1")]
        public void RecordMessagesInSendOrder()
        {
            // Act
            _sut.Power = 1.0;
            _sut.SetTemperature(20.0);
            _sut.Power = 2.0;

            // Assert
            var recorded = _context.GetSentMessagesOfTypePublic<object>();
            Assert.HasCount(3, recorded);
            Assert.IsInstanceOfType<ServicePropertyValueChanged>(recorded[0]);
            Assert.IsInstanceOfType<ServiceMeasuringPointValueChanged>(recorded[1]);
            Assert.IsInstanceOfType<ServicePropertyValueChanged>(recorded[2]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-004.1")]
        public void AnswerRecordedQueryFromMaterialisedCopy()
        {
            // Arrange
            _sut.Power = 1.0;
            var recorded = _context.GetSentMessagesOfTypePublic<ServicePropertyValueChanged>();

            // Act — a deferred query would see the clear and read empty
            _context.ClearRecordedMessages();

            // Assert
            Assert.HasCount(1, recorded);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-004.2")]
        public void AnswerActorLookupWithStandInOfRequestedName()
        {
            // Arrange
            IActorContext context = _context;

            // Act
            var reference = context.LookupByName("PersistentDataHandler");

            // Assert
            Assert.AreEqual("TestActorRef(PersistentDataHandler)", reference.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-004.3")]
        public void ClearRecordedMessages()
        {
            // Arrange
            _sut.Power = 1.0;
            Assert.IsNotEmpty(_context.GetSentMessagesOfTypePublic<object>());

            // Act
            _context.ClearRecordedMessages();

            // Assert
            Assert.IsEmpty(_context.GetSentMessagesOfTypePublic<object>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.1")]
        public void DefaultOccurrenceExpectationToExactlyOnce()
        {
            // Act
            _sut.Power = 1.0;

            // Assert
            _context.VerifyServicePropertyChanged(lb => lb.Power);
            _sut.Power = 2.0;
            Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyServicePropertyChanged(lb => lb.Power));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.4")]
        public void FilterServicePropertyVerificationToItsOwnMember()
        {
            // Act
            _sut.Power = 1.0;
            _sut.Counter = 5;

            // Assert
            _context.VerifyServicePropertyChanged(lb => lb.Power, value => Assert.AreEqual(1.0, value));
            _context.VerifyServicePropertyChanged(lb => lb.Counter, value => Assert.AreEqual(5, value));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.4")]
        public void FilterMeasuringPointVerificationToItsOwnMember()
        {
            // Act
            _sut.SetTemperature(22.5);

            // Assert
            _context.VerifyServiceMeasuringPointChanged(lb => lb.Temperature, value => Assert.AreEqual(22.5, value));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.4")]
        public void CountOnlyMemberVerificationNames()
        {
            // Act
            _sut.Power = 1.0;
            _sut.Power = 2.0;
            _sut.Counter = 5;

            // Assert
            _context.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Exactly(2));
            _context.VerifyServicePropertyChanged(lb => lb.Counter, times: Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.5")]
        public void RunPerMessageAssertionAgainstEveryMatch()
        {
            // Arrange
            var seen = 0;

            // Act
            _sut.Power = 1.0;
            _sut.Power = 2.0;
            _sut.Power = 3.0;

            // Assert — a helper that asserted only the first would see one
            _context.VerifyServicePropertyChanged(lb => lb.Power,
                                                  _ => seen++,
                                                  Times.Exactly(3));
            Assert.AreEqual(3, seen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.7")]
        public void ReturnRecordedMessagesOfRequestedType()
        {
            // Act
            _sut.Power = 1.0;
            _sut.SetTemperature(20.0);

            // Assert
            Assert.HasCount(1, _context.GetSentMessagesOfTypePublic<ServicePropertyValueChanged>());
            Assert.HasCount(1, _context.GetSentMessagesOfTypePublic<ServiceMeasuringPointValueChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.8")]
        public void RaiseOneExceptionTypeForEveryFailedAssertion()
        {
            // Act / Assert
            Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyServicePropertyChanged(lb => lb.Power));
            Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyServiceMeasuringPointChanged(lb => lb.Temperature));
            Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyContractMessageSent<FakeContractData>("probe"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.8")]
        public void VerifyTextLoggerRecordedAtGivenLevel()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();

            // Act
            loggerMock.Object.LogWarning("grid went invalid");

            // Assert
            loggerMock.VerifyLogContains("grid went invalid", LogLevel.Warning, Times.Once());
            loggerMock.VerifyLogContains("something else", LogLevel.Warning, Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.8")]
        public void VerifyTextTypedLoggerRecordedAtGivenLevel()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<SampleLogicBlock>>();

            // Act
            loggerMock.Object.LogWarning("grid went invalid");

            // Assert
            loggerMock.VerifyLogContains("grid went invalid", LogLevel.Warning, Times.Once());
            loggerMock.VerifyLogContains("something else", LogLevel.Warning, Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.6")]
        public void NameContractVerificationInFailureWithoutFilteringOnIt()
        {
            // Act / Assert — the argument is a label: it reaches the failure text and nothing else
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyContractMessageSent<FakeContractData>("AnyLabelAtAll"));
            StringAssert.StartsWith(thrown.Message, "AnyLabelAtAll verification failed");
        }

        /// <summary>A contract payload no fixture block sends, so a verification of it matches nothing.</summary>
        private readonly record struct FakeContractData(int Value);
    }
}
