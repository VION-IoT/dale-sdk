using System;
using Moq;

namespace Vion.Dale.Sdk.TestKit.Test
{
    [TestClass]
    public class LogicBlockTestContextShould
    {
        // --- Factory methods ---

        [TestMethod]
        public void CreateLogicBlockWithFactoryMethod()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();

            Assert.IsNotNull(block);
            Assert.IsInstanceOfType<SampleLogicBlock>(block);
        }

        [TestMethod]
        public void CreateLogicBlockWithLoggerMock()
        {
            var (block, loggerMock) = LogicBlockTestHelper.CreateWithLogger<SampleLogicBlock>();

            Assert.IsNotNull(block);
            Assert.IsNotNull(loggerMock);
        }

        // --- Initialization ---

        [TestMethod]
        public void InitializeLogicBlockForTest()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.InitializeForTest();

            Assert.IsNotNull(testContext);
        }

        [TestMethod]
        public void AutoStartByDefault()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 3.5;

            // Started blocks produce property change messages
            testContext.VerifyServicePropertyChanged(lb => lb.Power);
        }

        [TestMethod]
        public void AutoStartClearsInfrastructureMessages()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            // Auto-start publishes initial state for all properties, but those should be cleared.
            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Never());
        }

        [TestMethod]
        public void NotProducePropertyChangeMessagesWithoutAutoStart()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().WithoutAutoStart().Build();

            block.Power = 3.5;

            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Never());
        }

        // --- Service property verification ---

        [TestMethod]
        public void VerifyServicePropertyChanged()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 3.5;

            testContext.VerifyServicePropertyChanged(lb => lb.Power, value => Assert.AreEqual(3.5, value));
        }

        [TestMethod]
        public void VerifyServicePropertyChangedWithTimes()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 1.0;
            block.Power = 2.0;

            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Exactly(2));
        }

        [TestMethod]
        public void VerifyServicePropertyChangedOnlyCountsTargetProperty()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 3.5;
            block.Counter = 10;

            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Once());
            testContext.VerifyServicePropertyChanged(lb => lb.Counter, times: Times.Once());
        }

        // --- Service measuring point verification ---

        [TestMethod]
        public void VerifyServiceMeasuringPointChanged()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.SetTemperature(22.5);

            testContext.VerifyServiceMeasuringPointChanged(lb => lb.Temperature, value => Assert.AreEqual(22.5, value));
        }

        // --- Timer simulation ---

        [TestMethod]
        public void FireTimerByIdentifier()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            block.Counter = 0;
            block.FireTimer("OnPeriodicUpdate");

            Assert.AreEqual(1, block.Counter);
        }

        [TestMethod]
        public void FireTimerByExpression()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            block.Counter = 0;
            block.FireTimer(lb => lb.OnPeriodicUpdate());

            Assert.AreEqual(1, block.Counter);
        }

        [TestMethod]
        public void FireTimerMultipleTimes()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            block.Counter = 0;
            block.FireTimer(lb => lb.OnPeriodicUpdate());
            block.FireTimer(lb => lb.OnPeriodicUpdate());
            block.FireTimer(lb => lb.OnPeriodicUpdate());

            Assert.AreEqual(3, block.Counter);
        }

        [TestMethod]
        public void GetTimerInterval()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            var interval = block.GetTimerInterval("OnPeriodicUpdate");

            Assert.AreEqual(TimeSpan.FromSeconds(5), interval);
        }

        [TestMethod]
        public void GetTimerIntervalByExpression()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            var interval = block.GetTimerInterval(lb => lb.OnPeriodicUpdate());

            Assert.AreEqual(TimeSpan.FromSeconds(5), interval);
        }

        [TestMethod]
        public void ThrowWhenFiringUnknownTimer()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            Assert.ThrowsExactly<TestKitVerificationException>(() => block.FireTimer("NonExistent"));
        }

        // --- Persistent state ---

        [TestMethod]
        public void RestorePersistentState()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.CreateTestContext().WithPersistentValue(lb => lb.Power, 42.0).Build();

            Assert.AreEqual(42.0, block.Power);
        }

        [TestMethod]
        public void RestoreMultiplePersistentValues()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.CreateTestContext().WithPersistentValue(lb => lb.Power, 42.0).WithPersistentValue(lb => lb.Counter, 7).Build();

            Assert.AreEqual(42.0, block.Power);
            Assert.AreEqual(7, block.Counter);
        }

        // --- Message clearing ---

        [TestMethod]
        public void ClearRecordedMessages()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 3.5;
            testContext.ClearRecordedMessages();

            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Never());
        }

        // --- FlushPendingActions ---

        [TestMethod]
        public void FlushPendingActions_ExecuteDelayedAction()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleDelayedPowerUpdate(42.0);

            // Action is queued but not yet executed
            Assert.AreEqual(0.0, block.Power);

            testContext.FlushPendingActions();

            Assert.AreEqual(42.0, block.Power);
        }

        [TestMethod]
        public void FlushPendingActions_ExecuteMultipleActions()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleDelayedPowerUpdate(1.0);
            block.ScheduleDelayedPowerUpdate(2.0);
            block.ScheduleDelayedPowerUpdate(3.0);

            testContext.FlushPendingActions();

            // Last write wins
            Assert.AreEqual(3.0, block.Power);
        }

        [TestMethod]
        public void FlushPendingActions_SafeToCallWithNoPending()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            // Should not throw
            testContext.FlushPendingActions();
        }

        [TestMethod]
        public void FlushPendingActions_ClearsQueueAfterExecution()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleDelayedPowerUpdate(42.0);
            testContext.FlushPendingActions();

            block.Power = 0.0;
            testContext.FlushPendingActions(); // second flush should be a no-op

            Assert.AreEqual(0.0, block.Power);
        }
    }
}