using System;
using Moq;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.DigitalIo.TestKit.Test
{
    /// <summary>
    ///     The three verify helpers this kit ships, one per direction a block or a simulator writes in.
    ///     The mirror of <c>Vion.Dale.Sdk.AnalogIo.TestKit.Test.LogicBlockTestContextExtensionsShould</c>;
    ///     what differs is the value each face carries — a truth value has no near miss, so this file has
    ///     no tolerance rows and its analog twin has three.
    /// </summary>
    [TestClass]
    public class LogicBlockTestContextExtensionsShould
    {
        private LogicBlockTestContext<SampleProviderLogicBlock> _providerContext = null!;

        private SampleProviderLogicBlock _provider = null!;

        private LogicBlockTestContext<SampleLogicBlock> _context = null!;

        private SampleLogicBlock _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = LogicBlockTestHelper.Create<SampleLogicBlock>();
            _context = _sut.InitializeForTest();
            _provider = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
            _providerContext = _provider.InitializeForTest();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        [DataRow(true)]
        [DataRow(false)]
        public void AssertOutputWasSetWithValue(bool value)
        {
            // Act
            _sut.DigitalInput.RaiseInputChanged(value);

            // Assert
            _context.VerifyDigitalOutputSet(_sut.DigitalOutput, value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        [DataRow(true)]
        [DataRow(false)]
        public void AssertOutputProviderConfirmedValue(bool value)
        {
            // Act
            _provider.DigitalOutputProvider.RaiseSetReceived(value);

            // Assert
            _providerContext.VerifyDigitalOutputConfirmed(_provider.DigitalOutputProvider, value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        [DataRow(true)]
        [DataRow(false)]
        public void AssertInputProviderDroveValue(bool value)
        {
            // Act
            _provider.DigitalOutputProvider.RaiseSetReceived(value);

            // Assert
            _providerContext.VerifyDigitalInputDriven(_provider.DigitalInputProvider, value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void MatchAnyFaceOfKindWhenNoneNamed()
        {
            // Act
            _sut.DigitalInput.RaiseInputChanged(true);

            // Assert
            _context.VerifyDigitalOutputSet();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void MatchAnyValueWhenNoneNamed()
        {
            // Act
            _sut.DigitalInput.RaiseInputChanged(true);
            _sut.DigitalInput.RaiseInputChanged(false);

            // Assert
            _context.VerifyDigitalOutputSet(_sut.DigitalOutput, times: Times.Exactly(2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void CountNothingWhenBlockWroteNothing()
        {
            // Act / Assert
            _providerContext.VerifyDigitalOutputConfirmed(_provider.DigitalOutputProvider, times: Times.Never());
            _providerContext.VerifyDigitalInputDriven(_provider.DigitalInputProvider, times: Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void RefuseOutputFaceThatIsNotTheShippedImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyDigitalOutputSet(new Mock<IDigitalOutput>().Object));
            StringAssert.Contains(thrown.Message, "Unable to assert digital output state");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void RefuseOutputProviderFaceThatIsNotTheShippedImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => _providerContext.VerifyDigitalOutputConfirmed(new Mock<IDigitalOutputProvider>().Object));
            StringAssert.Contains(thrown.Message, "Unable to assert digital output provider confirmation");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void RefuseInputProviderFaceThatIsNotTheShippedImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => _providerContext.VerifyDigitalInputDriven(new Mock<IDigitalInputProvider>().Object));
            StringAssert.Contains(thrown.Message, "Unable to assert digital input provider drive");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(true)]
        [DataRow(false)]
        public void RejectValueThatDoesNotMatchWhatWasWritten(bool written)
        {
            // Arrange
            _sut.DigitalInput.RaiseInputChanged(written);

            // Act / Assert — the negative half of the comparison, without which every match above is
            // satisfied by a helper that compares nothing
            Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyDigitalOutputSet(_sut.DigitalOutput, !written));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(true)]
        [DataRow(false)]
        public void RejectConfirmationThatDoesNotMatchWhatWasConfirmed(bool confirmed)
        {
            // Arrange
            _provider.DigitalOutputProvider.RaiseSetReceived(confirmed);

            // Act / Assert
            Assert.ThrowsExactly<TestKitVerificationException>(() => _providerContext.VerifyDigitalOutputConfirmed(_provider.DigitalOutputProvider, !confirmed));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(true)]
        [DataRow(false)]
        public void RejectDriveThatDoesNotMatchWhatWasDriven(bool driven)
        {
            // Arrange
            _provider.DigitalOutputProvider.RaiseSetReceived(driven);

            // Act / Assert
            Assert.ThrowsExactly<TestKitVerificationException>(() => _providerContext.VerifyDigitalInputDriven(_provider.DigitalInputProvider, !driven));
        }
    }
}
