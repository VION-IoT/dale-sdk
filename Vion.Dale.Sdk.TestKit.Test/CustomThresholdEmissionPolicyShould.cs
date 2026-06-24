using System;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.TestKit.Test
{
    // DF-34: a custom struct value type with its IChangeThreshold<T> declared in the block's own
    // assembly. The runtime must discover the threshold by scanning that assembly at block start,
    // so a deadband on a non-built-in type actually gates emissions instead of silently no-op'ing.
    public readonly record struct Pressure(double Bar);

    public sealed class PressureChangeThreshold : IChangeThreshold<Pressure>
    {
        public bool Exceeds(in Pressure lastEmitted, in Pressure candidate, string threshold)
        {
            var min = double.Parse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture);
            return Math.Abs(candidate.Bar - lastEmitted.Bar) >= min;
        }
    }

    public class CustomThresholdLogicBlock : LogicBlockBase
    {
        [ServiceProperty(MinInterval = "250ms", MinChange = "2")]
        public Pressure Reading { get; set; }

        public CustomThresholdLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    // DF-33 + DF-34 combined — the consumer's planned §8.12 pattern: a custom-typed deadband declared
    // ONCE on a shared [ServiceInterface]. The knob must be inherited by the bare impl property (DF-33)
    // and its IChangeThreshold<T> resolved by scanning the interface property's declaring assembly (DF-34).
    [ServiceInterface]
    public interface ICustomThresholdService
    {
        [ServiceProperty(MinInterval = "250ms", MinChange = "2")]
        Pressure Reading { get; set; }
    }

    public class InterfaceCustomThresholdLogicBlock : LogicBlockBase, ICustomThresholdService
    {
        public InterfaceCustomThresholdLogicBlock(ILogger logger) : base(logger)
        {
        }

        // Bare impl — the custom-typed MinChange lives only on the interface.
        public Pressure Reading { get; set; }

        protected override void Ready()
        {
        }
    }

    public class UnresolvableMinChangeLogicBlock : LogicBlockBase
    {
        // bool has no magnitude, so no IChangeThreshold<bool> can exist. DALE034 normally errors at
        // compile time; suppress it to exercise the runtime fail-fast backstop that replaces the
        // silent no-op (DF-34, proposal #2).
#pragma warning disable DALE034
        [ServiceProperty(MinChange = "1")]
        public bool Flag { get; set; }
#pragma warning restore DALE034

        public UnresolvableMinChangeLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    [TestClass]
    public class CustomThresholdEmissionPolicyShould
    {
        [TestMethod]
        public void ApplyDeadbandResolvedByScanningTheBlockAssembly()
        {
            var block = LogicBlockTestHelper.Create<CustomThresholdLogicBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // clear the start-seed interval

            block.Reading = new Pressure(10.0); // leading edge -> emit
            block.Reading = new Pressure(11.0); // |Δ| = 1 < MinChange 2 -> deadband DROP (not held)

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // interval elapses

            // With the custom threshold resolved by assembly scan, the sub-threshold change is dropped,
            // nothing is held, and there is no trailing flush — only the leading-edge emit survives.
            // Before the fix the threshold was unresolved (silent no-op): 11.0 was held then flushed -> 2.
            ctx.VerifyServicePropertyEmitted(lb => lb.Reading, times: Times.Once());
        }

        [TestMethod]
        public void EmitWhenAChangeClearsTheCustomDeadband()
        {
            var block = LogicBlockTestHelper.Create<CustomThresholdLogicBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // clear the start-seed interval

            block.Reading = new Pressure(10.0); // leading edge -> emit
            block.Reading = new Pressure(13.0); // |Δ| = 3 >= MinChange 2 -> passes the deadband, held

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // interval elapses -> trailing flush of 13.0

            ctx.VerifyServicePropertyEmitted(lb => lb.Reading, times: Times.Exactly(2));
        }

        [TestMethod]
        public void ApplyInterfaceDeclaredDeadbandOnACustomType()
        {
            // Exercises both fixes: the interface-declared MinChange is inherited (DF-33) and its custom
            // IChangeThreshold<Pressure> is resolved by scanning the interface property's declaring
            // assembly (DF-34). Sub-threshold change is dropped, so only the leading edge emits.
            var block = LogicBlockTestHelper.Create<InterfaceCustomThresholdLogicBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // clear the start-seed interval

            block.Reading = new Pressure(10.0); // leading edge -> emit
            block.Reading = new Pressure(11.0); // |Δ| = 1 < MinChange 2 -> deadband DROP (not held)

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // interval elapses

            ctx.VerifyServicePropertyEmitted(lb => lb.Reading, times: Times.Once());
        }

        [TestMethod]
        public void ThrowAtStartWhenMinChangeHasNoResolvableThreshold()
        {
            var block = LogicBlockTestHelper.Create<UnresolvableMinChangeLogicBlock>();

            // BuildThrottlers runs at block start; an unresolved MinChange must fail fast (load-time),
            // not silently leave the deadband absent.
            Assert.ThrowsExactly<InvalidOperationException>(() => block.CreateTestContext().Build());
        }
    }
}