using System;
using System.Linq.Expressions;
using Vion.Dale.Sdk.TestKit.Test.TestHelpers;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     Firing a timer and reading its interval, each reachable by identifier or by a method-call
    ///     expression. The rows of every refusal are the two queries, because they are two entry points
    ///     into one rule and a guard added to one alone would pass half of it.
    /// </summary>
    [TestClass]
    public class LogicBlockTimerExtensionsShould
    {
        private SampleLogicBlock _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = LogicBlockTestHelper.Create<SampleLogicBlock>();
            _sut.InitializeForTest();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.8")]
        public void FireTimerCallbackSelectedByIdentifier()
        {
            // Act
            _sut.FireTimer(nameof(SampleLogicBlock.OnPeriodicUpdate));

            // Assert
            Assert.AreEqual(1, _sut.Counter);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.8")]
        public void FireTimerCallbackSelectedByMethodCall()
        {
            // Act
            _sut.FireTimer(lb => lb.OnPeriodicUpdate());

            // Assert
            Assert.AreEqual(1, _sut.Counter);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.8")]
        public void FireTimerOnceForEachCall()
        {
            // Act
            _sut.FireTimer(nameof(SampleLogicBlock.OnPeriodicUpdate));
            _sut.FireTimer(nameof(SampleLogicBlock.OnPeriodicUpdate));
            _sut.FireTimer(nameof(SampleLogicBlock.OnPeriodicUpdate));

            // Assert
            Assert.AreEqual(3, _sut.Counter);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.8")]
        public void LeaveTimerUnfiredWhenClockAdvancesPastItsInterval()
        {
            // Arrange — the timer's interval is 5 s; the advance goes well past it
            var context = LogicBlockTestHelper.Create<SampleLogicBlock>().CreateTestContext().Build();

            // Act
            context.AdvanceTime(TimeSpan.FromMinutes(10));

            // Assert — a timer is fired explicitly, never by the clock
            Assert.AreEqual(0, _sut.Counter);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.8")]
        public void ReportConfiguredIntervalSelectedByIdentifier()
        {
            // Act
            var interval = _sut.GetTimerInterval(nameof(SampleLogicBlock.OnPeriodicUpdate));

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(5), interval);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.8")]
        public void ReportConfiguredIntervalSelectedByMethodCall()
        {
            // Act
            var interval = _sut.GetTimerInterval(lb => lb.OnPeriodicUpdate());

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(5), interval);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.8")]
        [DataRow(false, DisplayName = "FireTimer")]
        [DataRow(true, DisplayName = "GetTimerInterval")]
        public void RefuseUnregisteredTimerNamingAvailableOnes(bool readInterval)
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => Query(readInterval, "NotATimer"));
            Assert.AreEqual($"No timer registered with identifier 'NotATimer'. Available timers: '{nameof(SampleLogicBlock.OnPeriodicUpdate)}'.", thrown.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.8")]
        [DataRow(false, DisplayName = "FireTimer")]
        [DataRow(true, DisplayName = "GetTimerInterval")]
        public void RefuseUnregisteredTimerOnBlockDeclaringNoneAtAll(bool readInterval)
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SchedulingLogicBlock>();
            block.InitializeForTest();

            // Act / Assert
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => readInterval ? block.GetTimerInterval("NotATimer") : Fire(block, "NotATimer"));
            Assert.AreEqual("No timer registered with identifier 'NotATimer'. Available timers: (none).", thrown.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.8")]
        [DataRow(false, DisplayName = "FireTimer")]
        [DataRow(true, DisplayName = "GetTimerInterval")]
        public void RefuseSelectorOtherThanMethodCall(bool readInterval)
        {
            // Arrange — an object creation is a legal Action lambda and is not a method call, which is
            // the shape the selector guard exists for
            Expression<Action<SampleLogicBlock>> notACall = _ => new object();

            // Act / Assert
            var thrown = Assert.ThrowsExactly<ArgumentException>(() => QueryByExpression(readInterval, notACall));
            StringAssert.Contains(thrown.Message, "Expression must be a method call");
        }

        private static TimeSpan Fire(SchedulingLogicBlock block, string identifier)
        {
            block.FireTimer(identifier);
            return TimeSpan.Zero;
        }

        private TimeSpan QueryByExpression(bool readInterval, Expression<Action<SampleLogicBlock>> selector)
        {
            if (readInterval)
            {
                return _sut.GetTimerInterval(selector);
            }

            _sut.FireTimer(selector);
            return TimeSpan.Zero;
        }

        private TimeSpan Query(bool readInterval, string identifier)
        {
            if (readInterval)
            {
                return _sut.GetTimerInterval(identifier);
            }

            _sut.FireTimer(identifier);
            return TimeSpan.Zero;
        }
    }
}