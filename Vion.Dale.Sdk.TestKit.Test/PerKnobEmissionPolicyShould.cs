using System;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    [ServiceInterface]
    public interface IPerKnobService
    {
        [ServiceProperty(MinInterval = "5s")]
        double Titled { get; set; }

        [ServiceProperty(MinInterval = "5s")]
        double Banded { get; set; }

        [ServiceProperty(Immediate = true)]
        double Urgent { get; set; }

        [ServiceProperty(MinInterval = "0", MinChange = "5")]
        double Unbanded { get; set; }
    }

    /// <summary>
    ///     A block that redeclares an interface member's attribute keeps every knob its own attribute does not
    ///     assign: each knob is taken from the implementation where it assigned it, else from the interface. In
    ///     every case the two sides assign different values, so a whole-attribute reading gives a different
    ///     observable.
    /// </summary>
    [TestClass]
    public class PerKnobEmissionPolicyShould
    {
        private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);

        private PerKnobBlock _block = null!;

        private LogicBlockTestContext<PerKnobBlock> _context = null!;

        [TestInitialize]
        public void Initialize()
        {
            _block = LogicBlockTestHelper.Create<PerKnobBlock>();
            _context = _block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Move past every interval the start publish seeded, so the first write below is a leading edge.
            _context.AdvanceTime(TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.3")]
        public void TakeInterfaceIntervalUnderKnobFreeImplementationAttribute()
        {
            // Arrange
            _block.Titled = 1.0;

            // Act — one second on, past the SDK's 250 ms and inside the interface's 5 s.
            _context.AdvanceTime(TimeSpan.FromSeconds(1));
            _block.Titled = 2.0;
            _context.AdvanceTime(DefaultInterval);

            // Assert — the second value is still held.
            _context.VerifyServicePropertyEmitted(lb => lb.Titled, value => Assert.AreEqual(1.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.3")]
        public void CombineKnobsAssignedOnEachSide()
        {
            // Arrange — the implementation assigns a deadband of 1, the interface an interval of 5 s. Inside
            // that interval 20 is replaced by 30 before anything is released.
            _block.Banded = 10.0;
            _context.AdvanceTime(TimeSpan.FromSeconds(1));
            _block.Banded = 20.0;
            _context.AdvanceTime(TimeSpan.FromSeconds(1));
            _block.Banded = 30.0;
            _context.AdvanceTime(TimeSpan.FromSeconds(5));

            // Act — a move of 0.5 from the last emitted 30, well after the interval has run.
            _block.Banded = 30.5;
            _context.AdvanceTime(TimeSpan.FromSeconds(5));

            // Assert — the SDK's 250 ms would have let 20 out, and no deadband would have let 30.5 out.
            _context.VerifyServicePropertyEmitted(lb => lb.Banded, value => CollectionAssert.Contains(new[] { 10.0, 30.0 }, value), Times.Exactly(2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.3")]
        public void TakeInterfaceImmediateUnderKnobFreeImplementationAttribute()
        {
            // Arrange / Act
            _block.Urgent = 1.0;
            _block.Urgent = 2.0;
            _block.Urgent = 3.0;

            // Assert
            _context.VerifyServicePropertyEmitted(lb => lb.Urgent, times: Times.Exactly(3));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.7")]
        public void CancelInterfaceDeadbandWithEmptyAssignment()
        {
            // Arrange — the interface assigns a deadband of 5 and an interval of 0 (disabled); the
            // implementation assigns "".

            // Act — three moves of 1, each inside the interface's deadband.
            _block.Unbanded = 1.0;
            _block.Unbanded = 2.0;
            _block.Unbanded = 3.0;

            // Assert
            _context.VerifyServicePropertyEmitted(lb => lb.Unbanded, times: Times.Exactly(3));
        }

        public sealed class PerKnobBlock : LogicBlockBase, IPerKnobService
        {
            public PerKnobBlock(ILogger logger) : base(logger)
            {
            }

            // Redeclared for its title alone: every knob is the interface's.
            [ServiceProperty(Title = "Titled")]
            public double Titled { get; set; }

            [ServiceProperty(MinChange = "1")]
            public double Banded { get; set; }

            [ServiceProperty(Title = "Urgent")]
            public double Urgent { get; set; }

            [ServiceProperty(MinChange = "")]
            public double Unbanded { get; set; }

            protected override void Ready()
            {
            }
        }
    }
}