using System;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.TestKit.Test
{
    // A custom struct value type with its IChangeThreshold<T> declared in the block's own assembly —
    // the shape the search must find, so a deadband on a type the SDK ships none for actually gates
    // emissions instead of silently doing nothing.
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

    // The same custom deadband declared ONCE on a shared [ServiceInterface]: the knob is inherited by
    // the bare implementing property, and its IChangeThreshold<T> is searched for in the assembly that
    // declares the interface rather than the one that declares the block.
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
        // silent no-op.
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

    /// <summary>
    ///     A deadband on a value type the SDK ships none for: the implementation is found beside the
    ///     member, gates it like any built-in, and its absence refuses the block's start rather than
    ///     leaving a declared deadband quietly doing nothing.
    /// </summary>
    [TestClass]
    public class CustomThresholdEmissionPolicyShould
    {
        private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-009.1")]
        [DataRow(11.0, 1, DisplayName = "inside the deadband, so nothing follows the leading edge")]
        [DataRow(13.0, 2, DisplayName = "clearing the deadband, so it is held and then released")]
        public void GateACustomTypedMemberOnItsFoundDeadband(double second, int expectedEmissions)
        {
            // Arrange — MinChange is 2, and the deadband for Pressure is declared in this assembly.
            var block = LogicBlockTestHelper.Create<CustomThresholdLogicBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();
            context.AdvanceTime(DefaultInterval);

            // Act
            block.Reading = new Pressure(10.0);
            block.Reading = new Pressure(second);
            context.AdvanceTime(DefaultInterval);

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Reading, times: Times.Exactly(expectedEmissions));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.5")]
        public void ApplyInterfaceDeclaredDeadbandOnCustomType()
        {
            // Arrange — the implementing property is bare, so both the knob and the assembly to search
            // come from the interface.
            var block = LogicBlockTestHelper.Create<InterfaceCustomThresholdLogicBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();
            context.AdvanceTime(DefaultInterval);

            // Act
            block.Reading = new Pressure(10.0);
            block.Reading = new Pressure(11.0);
            context.AdvanceTime(DefaultInterval);

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Reading, times: Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-003.1")]
        [TestProperty("spec", "AC-EMIT-003.2")]
        public void RefuseToStartWhenNoDeadbandResolves()
        {
            // Arrange — bool has no magnitude, so no deadband for it can exist.
            var block = LogicBlockTestHelper.Create<UnresolvableMinChangeLogicBlock>();

            // Act / Assert — the gates are built at start, so the misconfiguration surfaces there rather
            // than leaving a declared deadband absent for the life of the block.
            Assert.ThrowsExactly<InvalidOperationException>(() => block.CreateTestContext().Build());
        }
    }
}