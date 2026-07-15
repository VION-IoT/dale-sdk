using Vion.Dale.Sdk.DigitalIo.TestKit;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.Gating.LogicBlocks;
using Xunit;

namespace Vion.Examples.Gating.Test
{
    /// <summary>
    ///     Proves RFC 0016 config-time structural gating on <see cref="ChargingStationBlock" /> across every
    ///     gateable member kind and every parameter type. The TestKit applies each
    ///     <c>[InstantiationParameter]</c> through the same JSON channel a topology uses
    ///     (<c>WithInstantiationParameter</c>), before <c>Configure</c>, so the gates resolve against it. A
    ///     gated-out member is never bound: driving a gated component publishes nothing, and a gated-out
    ///     contract stays <c>null</c>. (Gated inter-block interface mappings are cross-block wiring, verified
    ///     end to end via the DevHost — see the README.)
    /// </summary>
    public class ChargingStationBlockShould
    {
        // ── Number gate: components + measuring points ──────────────────────────────────────────────

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PublishStateForExactlyTheConfiguredNumberOfChargePoints(int chargePointCount)
        {
            var block = LogicBlockTestHelper.Create<ChargingStationBlock>();
            var ctx = block.CreateTestContext().WithInstantiationParameter(lb => lb.ChargePointCount, chargePointCount).Build();

            // Drive all three C# components. Only the ones the parameter includes are bound as services, so
            // only they publish a property state change — the number of changes is the live-point count.
            block.Point1.Active = true;
            block.Point2.Active = true;
            block.Point3.Active = true;

            var livePoints = ctx.GetSentMessagesOfTypePublic<ServicePropertyValueChanged>().Count;
            Assert.Equal(chargePointCount, livePoints);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void ChartPowerForExactlyTheConfiguredNumberOfChargePoints(int chargePointCount)
        {
            var block = LogicBlockTestHelper.Create<ChargingStationBlock>();
            var ctx = block.CreateTestContext().WithInstantiationParameter(lb => lb.ChargePointCount, chargePointCount).Build();

            // With every point charging, one timer tick moves each included point's Power 0 -> 11 kW. The
            // measuring-point stream gates exactly like the property stream: only bound points emit, proving
            // that gating a component gates its ENTIRE service (both streams), not just its state property.
            block.Point1.Active = true;
            block.Point2.Active = true;
            block.Point3.Active = true;
            block.OnTick();

            var chartedPoints = ctx.GetSentMessagesOfTypePublic<ServiceMeasuringPointValueChanged>().Count;
            Assert.Equal(chargePointCount, chartedPoints);
        }

        // ── Enum gate: component (membership predicate) ─────────────────────────────────────────────

        [Theory]
        [InlineData(StationModel.Basic, 0)]
        [InlineData(StationModel.Plus, 1)]
        [InlineData(StationModel.Pro, 1)]
        public void IncludeLoadManagementOnlyForRicherModels(StationModel model, int expectedPublishes)
        {
            var block = LogicBlockTestHelper.Create<ChargingStationBlock>();
            var ctx = block.CreateTestContext().WithInstantiationParameter(lb => lb.Model, model).Build();

            // The Model membership gate ("Model in ['Plus', 'Pro']") includes LoadManagement only on the richer
            // tiers; on Basic it is unbound, so driving it publishes nothing.
            block.LoadManagement.MaxCurrent = 20.0;

            var publishes = ctx.GetSentMessagesOfTypePublic<ServicePropertyValueChanged>().Count;
            Assert.Equal(expectedPublishes, publishes);
        }

        // ── String gate: IO input contract ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData("EU", true)]
        [InlineData("UK", true)]
        [InlineData("US", false)]
        public void FitTheGridFrequencyGuardInputOnlyInEuAndUkRegions(string region, bool expectedPresent)
        {
            var block = LogicBlockTestHelper.Create<ChargingStationBlock>();

            block.CreateTestContext().WithInstantiationParameter(lb => lb.Region, region).Build();

            // The string membership gate ("Region in ['EU', 'UK']") includes the digital-input contract only in
            // those regions; elsewhere it is a gated-out contract and stays null.
            Assert.Equal(expectedPresent, block.GridFrequencyGuard != null);
        }

        [Fact]
        public void AlwaysBindTheUngatedMainContactor()
        {
            var block = LogicBlockTestHelper.Create<ChargingStationBlock>();

            block.CreateTestContext().WithInstantiationParameter(lb => lb.ChargePointCount, 1).Build();

            // The main contactor carries no [IncludedWhen] — it is the always-present IO baseline.
            Assert.NotNull(block.MainContactor);
        }

        [Fact]
        public void ApplyTheChargePointCountThroughTheBuilderBeforeConfigure()
        {
            var block = LogicBlockTestHelper.Create<ChargingStationBlock>();

            block.CreateTestContext().WithInstantiationParameter(lb => lb.ChargePointCount, 2).Build();

            Assert.Equal(2, block.ChargePointCount);
        }

        // ── Number gate: IO output contract (the null-when-excluded hazard) ─────────────────────────

        [Fact]
        public void BindAndDriveTheContactorContractWhenASecondBayExists()
        {
            var block = LogicBlockTestHelper.Create<ChargingStationBlock>();
            var ctx = block.CreateTestContext().WithInstantiationParameter(lb => lb.ChargePointCount, 2).Build();

            // Included: the binder constructed the contract, so it is non-null and drivable.
            Assert.NotNull(block.Bay2Contactor);

            block.Point2.Active = true;
            block.OnTick(); // OnTick mirrors Point2.Active onto the contactor

            ctx.VerifyDigitalOutputSet(block.Bay2Contactor!, true);
        }

        [Fact]
        public void LeaveTheContactorContractNullWhenThereIsNoSecondBay()
        {
            var block = LogicBlockTestHelper.Create<ChargingStationBlock>();

            block.CreateTestContext().WithInstantiationParameter(lb => lb.ChargePointCount, 1).Build();

            // Excluded: a gated-out contract property is never constructed — it stays null (the documented
            // hazard). OnTick's `Bay2Contactor?.Set(...)` null-guard is what keeps that safe at runtime.
            Assert.Null(block.Bay2Contactor);
        }
    }
}