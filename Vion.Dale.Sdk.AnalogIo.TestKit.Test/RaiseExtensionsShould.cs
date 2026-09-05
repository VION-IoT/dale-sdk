using System;
using Moq;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.AnalogIo.TestKit.Test
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
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void RaiseInputChangedCarryingValue(double value)
        {
            // Arrange
            var context = _sut.InitializeForTest();

            // Act
            _sut.AnalogInput.RaiseInputChanged(value);

            // Assert — the block doubles its input onto its output, so the output carries twice what was raised
            context.VerifyAnalogOutputSet(_sut.AnalogOutput, value * 2);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.1")]
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void RaiseOutputChangedCarryingValue(double value)
        {
            // Arrange
            _sut.InitializeForTest();

            // Act
            _sut.AnalogOutput.RaiseOutputChanged(value);

            // Assert
            Assert.AreEqual(value, _sut.LastOutputConfirmation);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.1")]
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void RaiseSetReceivedCarryingValue(double value)
        {
            // Arrange
            var context = _provider.InitializeForTest();

            // Act
            _provider.AnalogOutputProvider.RaiseSetReceived(value);

            // Assert
            context.VerifyAnalogOutputConfirmed(_provider.AnalogOutputProvider, value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.1")]
        public void RaiseOncePerCall()
        {
            // Arrange
            var context = _provider.InitializeForTest();

            // Act
            _provider.AnalogOutputProvider.RaiseSetReceived(1.5);
            _provider.AnalogOutputProvider.RaiseSetReceived(1.5);
            _provider.AnalogOutputProvider.RaiseSetReceived(2.5);

            // Assert
            context.VerifyAnalogOutputConfirmed(_provider.AnalogOutputProvider, 1.5, times: Times.Exactly(2));
            context.VerifyAnalogOutputConfirmed(_provider.AnalogOutputProvider, 2.5, times: Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseNullInputFace()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => ((IAnalogInput)null!).RaiseInputChanged(1.0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseNullOutputFace()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => ((IAnalogOutput)null!).RaiseOutputChanged(1.0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseNullOutputProviderFace()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => ((IAnalogOutputProvider)null!).RaiseSetReceived(1.0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseInputFaceThatIsNotTheShippedImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new Mock<IAnalogInput>().Object.RaiseInputChanged(1.0));
            StringAssert.Contains(thrown.Message, "Unable to raise InputChanged");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseOutputFaceThatIsNotTheShippedImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new Mock<IAnalogOutput>().Object.RaiseOutputChanged(1.0));
            StringAssert.Contains(thrown.Message, "Unable to raise OutputChanged");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-006.2")]
        public void RefuseOutputProviderFaceThatIsNotTheShippedImplementation()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new Mock<IAnalogOutputProvider>().Object.RaiseSetReceived(1.0));
            StringAssert.Contains(thrown.Message, "Unable to raise SetReceived");
        }

    }
}
