using System;
using Moq;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.DigitalIo.TestKit.Test
{
    /// <summary>
    ///     The three raise helpers this kit ships — one per face that declares an event. They are one
    ///     design three times, so they are tested as one family with the face as the row, the way
    ///     <c>docs/specs/io.md</c> states one rule for its four faces rather than four.
    ///     <para>
    ///         The mirror of <c>Vion.Dale.Sdk.AnalogIo.TestKit.Test.RaiseExtensionsShould</c>: the two files
    ///         differ only in the value type each carries and in what that value type implies.
    ///     </para>
    /// </summary>
    [TestClass]
    public class RaiseExtensionsShould
    {
        private SampleLogicBlock _sut = null!;

        private SampleProviderLogicBlock _provider = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = LogicBlockTestHelper.Create<SampleLogicBlock>();
            _provider = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.1")]
        [DataRow(true)]
        [DataRow(false)]
        public void RaiseInputChangedCarryingValue(bool value)
        {
            // Arrange
            var context = _sut.InitializeForTest();

            // Act
            _sut.DigitalInput.RaiseInputChanged(value);

            // Assert — the block echoes its input onto its output, so the output carries what was raised
            context.VerifyDigitalOutputSet(_sut.DigitalOutput, value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.1")]
        [DataRow(true)]
        [DataRow(false)]
        public void RaiseOutputChangedCarryingValue(bool value)
        {
            // Arrange
            _sut.InitializeForTest();

            // Act
            _sut.DigitalOutput.RaiseOutputChanged(value);

            // Assert
            Assert.AreEqual(value, _sut.LastOutputConfirmation);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.1")]
        [DataRow(true)]
        [DataRow(false)]
        public void RaiseSetReceivedCarryingValue(bool value)
        {
            // Arrange
            var context = _provider.InitializeForTest();

            // Act
            _provider.DigitalOutputProvider.RaiseSetReceived(value);

            // Assert
            context.VerifyDigitalOutputConfirmed(_provider.DigitalOutputProvider, value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.1")]
        public void RaiseOncePerCall()
        {
            // Arrange
            var context = _provider.InitializeForTest();

            // Act
            _provider.DigitalOutputProvider.RaiseSetReceived(true);
            _provider.DigitalOutputProvider.RaiseSetReceived(false);
            _provider.DigitalOutputProvider.RaiseSetReceived(true);

            // Assert
            context.VerifyDigitalOutputConfirmed(_provider.DigitalOutputProvider, true, Times.Exactly(2));
            context.VerifyDigitalOutputConfirmed(_provider.DigitalOutputProvider, false, Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseNullInputFace()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => ((IDigitalInput)null!).RaiseInputChanged(true));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseNullOutputFace()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => ((IDigitalOutput)null!).RaiseOutputChanged(true));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseNullOutputProviderFace()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => ((IDigitalOutputProvider)null!).RaiseSetReceived(true));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseInputFaceThatIsNotTheShippedImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new Mock<IDigitalInput>().Object.RaiseInputChanged(true));
            StringAssert.Contains(thrown.Message, "Unable to raise InputChanged");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseOutputFaceThatIsNotTheShippedImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new Mock<IDigitalOutput>().Object.RaiseOutputChanged(true));
            StringAssert.Contains(thrown.Message, "Unable to raise OutputChanged");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseOutputProviderFaceThatIsNotTheShippedImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new Mock<IDigitalOutputProvider>().Object.RaiseSetReceived(true));
            StringAssert.Contains(thrown.Message, "Unable to raise SetReceived");
        }

    }
}
