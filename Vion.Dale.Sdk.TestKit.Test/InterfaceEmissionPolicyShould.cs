using System;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     DF-33: a family of blocks declares its emission policy <em>once</em> on a shared
    ///     <c>[ServiceInterface]</c> (the §8.12 DRY convention), and the implementing block carries
    ///     only the bare property. The runtime must honour the interface-declared throttle knobs the
    ///     same way it already merges the interface-declared schema — impl wins if it carries its own
    ///     attribute, otherwise the interface's knobs apply. Before the fix the knobs were read only
    ///     from the impl property, so no throttler was built and every assignment emitted raw.
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
        [TestMethod]
        public void ThrottleServicePropertyUsingKnobsDeclaredOnTheInterface()
        {
            var block = LogicBlockTestHelper.Create<InterfaceEmissionLogicBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // clear the start-seed interval

            block.Reading = 1.0; // leading edge -> emit
            block.Reading = 2.0; // within 250ms -> held
            block.Reading = 3.0; // within 250ms -> held (latest wins)

            // With the interface fallback the 250ms throttle applies: one leading-edge emit, the rest held.
            // Before the fix no throttler is built (impl carries no attribute) and all three emit raw.
            ctx.VerifyServicePropertyEmitted(lb => lb.Reading, times: Times.Once());
        }

        [TestMethod]
        public void ThrottleMeasuringPointUsingKnobsDeclaredOnTheInterface()
        {
            var block = LogicBlockTestHelper.Create<InterfaceEmissionLogicBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // clear the start-seed interval

            block.SetFrequency(50.0); // leading edge -> emit
            block.SetFrequency(50.1); // held
            block.SetFrequency(50.2); // held (latest wins)

            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.Frequency, times: Times.Once());
        }
    }
}