using System;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     A family of blocks declares its emission policy <em>once</em> on a shared
    ///     <c>[ServiceInterface]</c> and the implementing block carries only the bare property. The
    ///     interface-declared knobs are honoured the same way the interface-declared schema already is:
    ///     the implementation wins where it declares the stream's own attribute, otherwise the
    ///     interface's knobs apply.
    /// </summary>
    [ServiceInterface]
    public interface IInterfaceEmissionService
    {
        // Emission knobs declared ONCE on the interface; the impl property carries none.
        [ServiceProperty(MinInterval = "250ms")]
        double Reading { get; set; }

        [ServiceMeasuringPoint(MinInterval = "250ms")]
        double Frequency { get; }
    }

    public class InterfaceEmissionLogicBlock : LogicBlockBase, IInterfaceEmissionService
    {
        public InterfaceEmissionLogicBlock(ILogger logger) : base(logger)
        {
        }

        // No [ServiceProperty] / [ServiceMeasuringPoint] here — the knobs live only on the interface.
        public double Reading { get; set; }

        public double Frequency { get; private set; }

        public void SetFrequency(double value)
        {
            Frequency = value;
        }

        protected override void Ready()
        {
        }
    }

    [TestClass]
    public class InterfaceEmissionPolicyShould
    {
        private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);

        private InterfaceEmissionLogicBlock _block = null!;

        private LogicBlockTestContext<InterfaceEmissionLogicBlock> _context = null!;

        [TestInitialize]
        public void Initialize()
        {
            _block = LogicBlockTestHelper.Create<InterfaceEmissionLogicBlock>();
            _context = _block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Move past the interval the start publish seeded, so the first write below is a leading edge.
            _context.AdvanceTime(DefaultInterval);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.3")]
        public void ThrottleServicePropertyFromInterfaceKnobs()
        {
            // Arrange / Act
            _block.Reading = 1.0;
            _block.Reading = 2.0;
            _block.Reading = 3.0;

            // Assert — with no attribute on the implementation, no gate would be built at all and every
            // write would reach the handler.
            _context.VerifyServicePropertyEmitted(lb => lb.Reading, value => Assert.AreEqual(1.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.3")]
        public void ThrottleMeasuringPointFromInterfaceKnobs()
        {
            // Arrange / Act
            _block.SetFrequency(50.0);
            _block.SetFrequency(50.1);
            _block.SetFrequency(50.2);

            // Assert
            _context.VerifyServiceMeasuringPointEmitted(lb => lb.Frequency, value => Assert.AreEqual(50.0, value), Times.Once());
        }
    }
}