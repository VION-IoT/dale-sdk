using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.TestKit.Test.TestHelpers;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     The two drivers a test moves a block's scheduled work with, and the clock underneath them.
    ///     Every test here drives a fixture block that records which of its actions ran; nothing reaches a
    ///     runtime, a broker, a device or the development host, and no assertion is a wall-clock bound —
    ///     the clock the assertions read is the virtual one.
    /// </summary>
    [TestClass]
    public class VirtualTimeShould
    {
        private static readonly DateTime Anchor = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private LogicBlockTestContext<SchedulingLogicBlock> _context = null!;

        private SchedulingLogicBlock _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = LogicBlockTestHelper.Create<SchedulingLogicBlock>();
            _context = _sut.CreateTestContext().Build();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.1")]
        public void AnchorVirtualClockAtFirstInstantOf2026()
        {
            // Assert
            Assert.AreEqual(Anchor, _context.VirtualNow);
            Assert.AreEqual(Anchor, _context.TimeProvider.GetUtcNow().UtcDateTime);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.1")]
        public void ResolveContextClockAsBlocksTimeProvider()
        {
            // Assert
            Assert.AreSame(_context.TimeProvider, _context.BuiltServiceProvider!.GetService(typeof(TimeProvider)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.1")]
        public void BindCallerSuppliedClockToBothBlockAndDeadlines()
        {
            // Arrange
            var clock = new FakeTimeProvider(new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero));
            var block = LogicBlockTestHelper.Create<SchedulingLogicBlock>();

            // Act
            var context = block.CreateTestContext().WithTimeProvider(clock).Build();

            // Assert
            Assert.AreSame(clock, context.TimeProvider);
            Assert.AreSame(clock, context.BuiltServiceProvider!.GetService(typeof(TimeProvider)));
            Assert.AreEqual(new DateTime(2030, 6, 1, 0, 0, 0, DateTimeKind.Utc), context.VirtualNow);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.1")]
        public void GiveBlockServiceRegisteredClockWhileDrivingDeadlinesOnItsOwn()
        {
            // Arrange — the trap this criterion exists to state: a clock registered through WithServices
            // reaches the block and never the driver.
            var registered = new FakeTimeProvider(new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero));
            var block = LogicBlockTestHelper.Create<SchedulingLogicBlock>();

            // Act
            var context = block.CreateTestContext().WithServices(services => services.AddSingleton<TimeProvider>(registered)).Build();

            // Assert
            Assert.AreSame(registered, context.BuiltServiceProvider!.GetService(typeof(TimeProvider)));
            Assert.AreNotSame(registered, context.TimeProvider);
            Assert.AreEqual(Anchor, context.VirtualNow);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.2")]
        public void DispatchActionsInDeadlineOrderWhateverOrderTheyWereQueued()
        {
            // Arrange
            _sut.Schedule("late", TimeSpan.FromSeconds(9));
            _sut.Schedule("early", TimeSpan.FromSeconds(3));

            // Act
            _context.AdvanceTime(TimeSpan.FromSeconds(10));

            // Assert
            CollectionAssert.AreEqual(new[] { "early", "late" }, _sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.2")]
        public void DispatchActionsSharingDeadlineInEnqueueOrder()
        {
            // Arrange
            _sut.Schedule("first", TimeSpan.FromSeconds(5));
            _sut.Schedule("second", TimeSpan.FromSeconds(5));

            // Act
            _context.AdvanceTime(TimeSpan.FromSeconds(5));

            // Assert
            CollectionAssert.AreEqual(new[] { "first", "second" }, _sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.2")]
        public void SetClockToEachActionsOwnDeadlineBeforeRunningIt()
        {
            // Arrange — the block reads the clock the context drives, which is the shape WithTimeProvider exists for
            var clock = new FakeTimeProvider(new DateTimeOffset(Anchor, TimeSpan.Zero));
            var block = new ClockReadingLogicBlock(LogicBlockTestHelper.CreateLoggerMock().Object, clock);
            var context = block.CreateTestContext().WithTimeProvider(clock).Build();
            block.ScheduleClockRead(TimeSpan.FromSeconds(2));
            block.ScheduleClockRead(TimeSpan.FromSeconds(7));

            // Act
            context.AdvanceTime(TimeSpan.FromSeconds(10));

            // Assert
            CollectionAssert.AreEqual(new List<DateTime> { Anchor.AddSeconds(2), Anchor.AddSeconds(7) }, block.ObservedInstants);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.2")]
        public void DispatchActionQueuedDuringAdvanceWhoseDeadlineItStillReaches()
        {
            // Arrange
            _sut.ScheduleCascading("outer", TimeSpan.FromSeconds(2), "inner", TimeSpan.FromSeconds(3));

            // Act
            _context.AdvanceTime(TimeSpan.FromSeconds(10));

            // Assert
            CollectionAssert.AreEqual(new[] { "outer", "inner" }, _sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.2")]
        public void LeaveActionQueuedWhoseDeadlineLiesBeyondAdvance()
        {
            // Arrange
            _sut.Schedule("beyond", TimeSpan.FromSeconds(30));

            // Act
            _context.AdvanceTime(TimeSpan.FromSeconds(10));

            // Assert
            Assert.IsEmpty(_sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.2")]
        public void DispatchActionWhoseDeadlineAdvanceReachesExactly()
        {
            // Arrange
            _sut.Schedule("onTheDot", TimeSpan.FromSeconds(5));

            // Act
            _context.AdvanceTime(TimeSpan.FromSeconds(5));

            // Assert
            CollectionAssert.AreEqual(new[] { "onTheDot" }, _sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.4")]
        public void LeaveClockAtRequestedInstantWhenQueueEmpty()
        {
            // Act
            _context.AdvanceTime(TimeSpan.FromSeconds(10));

            // Assert
            Assert.AreEqual(Anchor.AddSeconds(10), _context.VirtualNow);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.4")]
        public void LeaveClockAtRequestedInstantAfterDispatchingEverythingDue()
        {
            // Arrange
            _sut.Schedule("due", TimeSpan.FromSeconds(3));

            // Act
            _context.AdvanceTime(TimeSpan.FromSeconds(10));

            // Assert
            Assert.AreEqual(Anchor.AddSeconds(10), _context.VirtualNow);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.4")]
        public void LeaveClockAtRequestedInstantWhenDispatchedActionThrows()
        {
            // Arrange
            _sut.ScheduleThrowing("boom", TimeSpan.FromSeconds(5));
            _sut.Schedule("later", TimeSpan.FromSeconds(8));

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _context.AdvanceTime(TimeSpan.FromSeconds(10)));
            Assert.AreEqual(Anchor.AddSeconds(10), _context.VirtualNow);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.4")]
        public void LeaveActionsAdvanceDidNotReachQueuedWhenDispatchedActionThrows()
        {
            // Arrange
            _sut.ScheduleThrowing("boom", TimeSpan.FromSeconds(5));
            _sut.Schedule("later", TimeSpan.FromSeconds(8));
            Assert.ThrowsExactly<InvalidOperationException>(() => _context.AdvanceTime(TimeSpan.FromSeconds(10)));

            // Act — the clock already stands at the requested instant, so the survivor is due now
            _context.AdvanceTime(TimeSpan.Zero);

            // Assert
            CollectionAssert.AreEqual(new[] { "boom", "later" }, _sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.4")]
        public void LeaveClockWhereDispatchedActionPutItWhenThatIsPastRequestedInstant()
        {
            // Arrange — the sibling of the landing rule: a fake that consumes virtual time of its own
            // (the Modbus TCP fake's response delay does) can carry the clock past the advance's target,
            // and the clock refuses to move backwards.
            var clock = new FakeTimeProvider(new DateTimeOffset(Anchor, TimeSpan.Zero));
            var block = new ClockReadingLogicBlock(LogicBlockTestHelper.CreateLoggerMock().Object, clock);
            var context = block.CreateTestContext().WithTimeProvider(clock).Build();
            block.ScheduleClockConsumer(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

            // Act
            context.AdvanceTime(TimeSpan.FromSeconds(5));

            // Assert
            Assert.AreEqual(Anchor.AddSeconds(31), context.VirtualNow);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.3")]
        public void RunActionWhoseDeadlineAlreadyPassedAtCurrentVirtualTime()
        {
            // Arrange — advance first, then queue at a deadline the clock has gone past
            _context.AdvanceTime(TimeSpan.FromSeconds(10));
            _sut.Schedule("stale", TimeSpan.FromSeconds(-4));

            // Act
            _context.AdvanceTime(TimeSpan.Zero);

            // Assert
            CollectionAssert.AreEqual(new[] { "stale" }, _sut.Ran);
            Assert.AreEqual(Anchor.AddSeconds(10), _context.VirtualNow);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.6")]
        public void RefuseAdvanceThatWouldMoveClockBackwards()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _context.AdvanceTime(TimeSpan.FromSeconds(-1)));
            Assert.AreEqual(Anchor, _context.VirtualNow);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.6")]
        public void RefuseAdvanceEnteredFromInsideDispatchedAction()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<ReentrantLogicBlock>();
            var context = block.CreateTestContext().Build();
            block.ScheduleReentrantAdvance(context);

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => context.AdvanceTime(TimeSpan.Zero));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.6")]
        public void RefuseFlushEnteredFromInsideDispatchedAction()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<ReentrantLogicBlock>();
            var context = block.CreateTestContext().Build();
            block.ScheduleReentrantFlush(context);

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => context.AdvanceTime(TimeSpan.Zero));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.5")]
        public void RunEveryQueuedActionOnFlushWhateverItsDeadline()
        {
            // Arrange
            _sut.Schedule("soon", TimeSpan.FromSeconds(1));
            _sut.Schedule("distant", TimeSpan.FromHours(9));

            // Act
            _context.FlushPendingActions();

            // Assert
            CollectionAssert.AreEqual(new[] { "soon", "distant" }, _sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.5")]
        public void LeaveClockUnmovedByFlush()
        {
            // Arrange
            _sut.Schedule("distant", TimeSpan.FromHours(9));

            // Act
            _context.FlushPendingActions();

            // Assert
            Assert.AreEqual(Anchor, _context.VirtualNow);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.5")]
        public void DeferActionQueuedDuringFlushToNextFlush()
        {
            // Arrange
            _sut.ScheduleCascading("outer", TimeSpan.Zero, "inner", TimeSpan.Zero);

            // Act
            _context.FlushPendingActions();

            // Assert
            CollectionAssert.AreEqual(new[] { "outer" }, _sut.Ran);
            _context.FlushPendingActions();
            CollectionAssert.AreEqual(new[] { "outer", "inner" }, _sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.5")]
        public void LeaveActionsFlushDidNotReachQueuedWhenDispatchedActionThrows()
        {
            // Arrange
            _sut.ScheduleThrowing("boom", TimeSpan.Zero);
            _sut.Schedule("later", TimeSpan.Zero);
            Assert.ThrowsExactly<InvalidOperationException>(() => _context.FlushPendingActions());

            // Act
            _context.FlushPendingActions();

            // Assert
            CollectionAssert.AreEqual(new[] { "boom", "later" }, _sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.7")]
        public void EnqueueActionScheduledWithoutDelaySoEitherDriverRunsIt()
        {
            // Arrange
            _sut.ScheduleImmediate("immediate");

            // Act
            _context.FlushPendingActions();

            // Assert
            CollectionAssert.AreEqual(new[] { "immediate" }, _sut.Ran);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.7")]
        public void StampDelayedActionsDeadlineFromClockAtScheduleTime()
        {
            // Arrange — the clock moves before the schedule, so a deadline stamped from the anchor
            // instead of from "now" would already be due
            _context.AdvanceTime(TimeSpan.FromSeconds(100));
            _sut.Schedule("relative", TimeSpan.FromSeconds(5));

            // Act
            _context.AdvanceTime(TimeSpan.FromSeconds(4));

            // Assert
            Assert.IsEmpty(_sut.Ran);
            _context.AdvanceTime(TimeSpan.FromSeconds(1));
            CollectionAssert.AreEqual(new[] { "relative" }, _sut.Ran);
        }
    }
}
