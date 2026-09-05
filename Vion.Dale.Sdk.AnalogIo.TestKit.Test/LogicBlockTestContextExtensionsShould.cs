using System;
using Moq;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.AnalogIo.TestKit.Test
{
    /// <summary>
    ///     The three verify helpers this kit ships, one per direction a block or a simulator writes in.
    ///     The mirror of <c>Vion.Dale.Sdk.AnalogIo.TestKit.Test.LogicBlockTestContextExtensionsShould</c>;
    ///     what differs is the value each face carries — a real number has near misses, so this file adds
    ///     the tolerance rows its digital twin has no need of.
    /// </summary>
    [TestClass]
    public class LogicBlockTestContextExtensionsShould
    {
        private LogicBlockTestContext<SampleLogicBlock> _context = null!;

        private SampleProviderLogicBlock _provider = null!;

        private LogicBlockTestContext<SampleProviderLogicBlock> _providerContext = null!;

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
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void AssertOutputWasSetWithValue(double value)
        {
            // Act
            _sut.AnalogInput.RaiseInputChanged(value);

            // Assert — the block doubles what its input reports
            _context.VerifyAnalogOutputSet(_sut.AnalogOutput, value * 2);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void AssertOutputProviderConfirmedValue(double value)
        {
            // Act
            _provider.AnalogOutputProvider.RaiseSetReceived(value);

            // Assert
            _providerContext.VerifyAnalogOutputConfirmed(_provider.AnalogOutputProvider, value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void AssertInputProviderDroveValue(double value)
        {
            // Act
            _provider.AnalogOutputProvider.RaiseSetReceived(value);

            // Assert
            _providerContext.VerifyAnalogInputDriven(_provider.AnalogInputProvider, value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void MatchAnyFaceOfKindWhenNoneNamed()
        {
            // Act
            _sut.AnalogInput.RaiseInputChanged(1.0);

            // Assert
            _context.VerifyAnalogOutputSet();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void MatchAnyValueWhenNoneNamed()
        {
            // Act
            _sut.AnalogInput.RaiseInputChanged(1.0);
            _sut.AnalogInput.RaiseInputChanged(2.0);

            // Assert
            _context.VerifyAnalogOutputSet(_sut.AnalogOutput, times: Times.Exactly(2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void CountNothingWhenBlockWroteNothing()
        {
            // Act / Assert
            _providerContext.VerifyAnalogOutputConfirmed(_provider.AnalogOutputProvider, times: Times.Never());
            _providerContext.VerifyAnalogInputDriven(_provider.AnalogInputProvider, times: Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void RefuseOutputFaceOfForeignImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyAnalogOutputSet(new Mock<IAnalogOutput>().Object));
            StringAssert.Contains(thrown.Message, "Unable to assert analog output state");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void RefuseOutputProviderFaceOfForeignImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => _providerContext.VerifyAnalogOutputConfirmed(new Mock<IAnalogOutputProvider>().Object));
            StringAssert.Contains(thrown.Message, "Unable to assert analog output provider confirmation");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.1")]
        public void RefuseInputProviderFaceOfForeignImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => _providerContext.VerifyAnalogInputDriven(new Mock<IAnalogInputProvider>().Object));
            StringAssert.Contains(thrown.Message, "Unable to assert analog input provider drive");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void RejectValueThatDoesNotMatchWhatWasWritten(double written)
        {
            // Arrange
            _sut.AnalogInput.RaiseInputChanged(written);

            // Act / Assert — the negative half of the comparison, without which every match above is
            // satisfied by a helper that compares nothing
            Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyAnalogOutputSet(_sut.AnalogOutput, written * 2 + 1));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void RejectConfirmationThatDoesNotMatchWhatWasConfirmed(double confirmed)
        {
            // Arrange
            _provider.AnalogOutputProvider.RaiseSetReceived(confirmed);

            // Act / Assert
            Assert.ThrowsExactly<TestKitVerificationException>(() => _providerContext.VerifyAnalogOutputConfirmed(_provider.AnalogOutputProvider, confirmed + 1));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void RejectDriveThatDoesNotMatchWhatWasDriven(double driven)
        {
            // Arrange
            _provider.AnalogOutputProvider.RaiseSetReceived(driven);

            // Act / Assert
            Assert.ThrowsExactly<TestKitVerificationException>(() => _providerContext.VerifyAnalogInputDriven(_provider.AnalogInputProvider, driven + 1));
        }
    }
}