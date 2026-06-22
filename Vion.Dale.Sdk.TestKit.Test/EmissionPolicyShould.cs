using System;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    [TestClass]
    public class EmissionPolicyShould
    {
        // A block with a default-policy (250ms) throttled property.
        private sealed class ThrottledBlock : LogicBlockBase
        {
            [ServiceProperty(MinInterval = "250ms")]
            public double Voltage { get; set; }

            public ThrottledBlock(ILogger logger) : base(logger)
            {
            }

            protected override void Ready()
            {
            }
        }

        [TestMethod]
        public void NotThrottleUnderFakeClockWithoutOverride()
        {
            // TestKit hosts a FakeTimeProvider (controllable clock) and no marker => policy OFF.
            // Every distinct change must reach the handler as raw INPC (today's behaviour).
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var ctx = block.CreateTestContext().Build();

            block.Voltage = 1.0;
            block.Voltage = 2.0;
            block.Voltage = 3.0;

            ctx.VerifyServicePropertyChanged(lb => lb.Voltage, times: Times.Exactly(3));
        }

        [TestMethod]
        public void EmitLeadingEdgeThenHoldUnderForcedPolicy()
        {
            // Force policy ON (override the FakeTimeProvider clock-off). The first change emits
            // immediately; further changes inside MinInterval are held (no second emit yet).
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // The initial publish at start seeds the throttler (leading edge) at virtual T0, then is
            // cleared by the builder. Advance past the interval so the first user write below is a
            // fresh leading edge rather than a same-instant hold.
            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250));

            block.Voltage = 1.0; // leading edge -> emit
            block.Voltage = 2.0; // within 250ms -> held
            block.Voltage = 3.0; // within 250ms -> held (latest wins)

            ctx.VerifyServicePropertyEmitted(lb => lb.Voltage, times: Times.Once());
        }

        [TestMethod]
        public void DropEqualValuesUnderForcedPolicy()
        {
            // Value-equality floor: re-emitting the same value is dropped even on the leading edge.
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // clear the start-seed interval

            block.Voltage = 5.0; // leading edge -> emit
            // Metalama dedups exact-equal sets for value types, so re-assigning 5.0 raises no INPC;
            // the gate never even sees the duplicate. The single leading-edge emit is all that is seen.
            block.Voltage = 5.0; // no INPC (Metalama) — gate never sees it

            ctx.VerifyServicePropertyEmitted(lb => lb.Voltage, value => Assert.AreEqual(5.0, value), Times.Once());
        }

        [TestMethod]
        public void FlushHeldValueAfterMinInterval()
        {
            // Leading edge emits 1.0; 2.0 then 3.0 are held within the 250ms window; after the
            // interval elapses the latest held value (3.0) flushes exactly once (trailing edge).
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // clear the start-seed interval

            block.Voltage = 1.0; // emit
            block.Voltage = 2.0; // held
            block.Voltage = 3.0; // held (latest wins)

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250));

            // 1.0 (leading) + 3.0 (trailing flush) = 2 emissions; last value is 3.0.
            ctx.VerifyServicePropertyEmitted(lb => lb.Voltage, times: Times.Exactly(2));
        }

        [TestMethod]
        public void ForceEmitInitialPublishUnderForcedPolicy()
        {
            // Do not auto-start (which clears startup messages). Start manually and assert the
            // initial publish for a throttled property emits exactly once (seed + force-emit),
            // not held, under the forced policy.
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var ctx = block.CreateTestContext()
                           .WithEmissionPolicy(EmissionPolicyMode.FromAttributes)
                           .WithoutAutoStart()
                           .Build();

            block.Voltage = 7.0; // not started yet -> ignored by the _started guard

            // Manually start: PublishInitialStateUpdates runs through the gate. The first Offer per
            // property returns Emit (!HasEmitted), force-emitting and seeding the throttler.
            block.HandleMessageAsync(new Vion.Dale.Sdk.Messages.StartLogicBlockRequest(), ctx).GetAwaiter().GetResult();

            ctx.VerifyServicePropertyEmitted(lb => lb.Voltage, value => Assert.AreEqual(7.0, value), Times.Once());
        }

        [TestMethod]
        public void EmitClearedImmediatelyAndCancelPendingFlush()
        {
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250)); // clear the start-seed interval

            block.Voltage = 1.0; // emit (leading)
            block.Voltage = 2.0; // held

            // Simulate the runtime stop-clear path: ClearRetainedMessages raises the Cleared events.
            var binder = GetServiceBinder(block);
            binder.ClearRetainedMessages(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

            // The clear is emitted (bypassing the gate) ...
            var cleared = ctx.GetSentMessagesOfTypePublic<Vion.Dale.Sdk.Messages.ServicePropertyValueCleared>();
            Assert.IsTrue(cleared.Count >= 1, "Cleared message must be emitted immediately, bypassing the throttler.");

            // ... and the previously-held flush is cancelled: advancing past the interval emits no held value.
            ctx.ClearRecordedMessages();
            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250));
            ctx.VerifyServicePropertyEmitted(lb => lb.Voltage, times: Times.Never());
        }

        // The TestKit's GetPrivateField extension is internal to the TestKit assembly; reach the
        // block's ServiceBinder via reflection here (ServiceBinder is a public SDK type).
        private static Vion.Dale.Sdk.Configuration.Services.ServiceBinder GetServiceBinder(LogicBlockBase block)
        {
            var field = typeof(LogicBlockBase).GetField("_serviceBinder",
                                                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?? throw new System.InvalidOperationException("_serviceBinder field not found on LogicBlockBase.");
            return (Vion.Dale.Sdk.Configuration.Services.ServiceBinder)field.GetValue(block)!;
        }
    }
}
