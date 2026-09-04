using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Configuration.Timers;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Configuration.Timers
{
    /// <summary>
    ///     What a <c>[Timer]</c> declaration has to satisfy before the block can be configured, and what the
    ///     binder registers when it does. The binder is driven directly here rather than through a
    ///     configuration message, because the subject is which declarations it accepts; what a registered
    ///     timer then does at runtime is <c>LogicBlockTimerShould</c>'s.
    ///     <para>
    ///         Three of these rules also have a compile-time door — the analyzer's <c>DALE002</c>,
    ///         <c>DALE005</c> and <c>DALE012</c> — and the interval has a third, the attribute's own
    ///         constructor. The criterion is over-determined on purpose: a warning-level diagnostic can be
    ///         suppressed and the block still ships, and then the binder is the only guard left.
    ///     </para>
    /// </summary>
    [TestClass]
    public sealed class DeclarativeTimerBinderShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.1")]
        public void RegisterTimerUnderDeclaredIdentifier()
        {
            // Arrange
            var factory = new RecordingTimerFactory();

            // Act
            DeclarativeTimerBinder.BindTimersFromAttributes(new NamedTimerBlock(), factory);

            // Assert
            Assert.AreEqual("cycle", factory.Registered.Single().Identifier, "An identifier the attribute names is what the timer ticks under.");
            Assert.AreEqual(TimeSpan.FromSeconds(2.5), factory.Registered.Single().Interval, "So is the interval it declares.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.1")]
        public void RegisterTimerUnderMethodNameWithoutDeclaredIdentifier()
        {
            // Arrange
            var factory = new RecordingTimerFactory();

            // Act
            DeclarativeTimerBinder.BindTimersFromAttributes(new UnnamedTimerBlock(), factory);

            // Assert
            Assert.AreEqual(nameof(UnnamedTimerBlock.Tick), factory.Registered.Single().Identifier, "A timer that names no identifier ticks under its method's name.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.2")]
        public void RefuseTimerMethodWithReturnValueOrParameters()
        {
            // Arrange
            var factory = new RecordingTimerFactory();

            // Act / Assert
            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => DeclarativeTimerBinder.BindTimersFromAttributes(new BadSignatureTimerBlock(), factory));
            StringAssert.Contains(refusal.Message, "ReturnsSomething", "The refusal names the method, which is the only thing the author can go and edit.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.2")]
        [DynamicData(nameof(UnschedulableIntervals))]
        public void RefuseIntervalNoTimerCanBeScheduledAt(object block, string reason)
        {
            // Arrange
            var factory = new RecordingTimerFactory();

            // Act / Assert
            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => DeclarativeTimerBinder.BindTimersFromAttributes(block, factory), reason);
            StringAssert.Contains(refusal.Message, "Tick", "The refusal names the method.");
            StringAssert.Contains(refusal.Message, "interval no timer can be scheduled at", "And says which of a timer's two declarations was wrong.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.2")]
        [DynamicData(nameof(BlankIdentifiers))]
        public void RefuseIdentifierWithNothingInIt(object block, string reason)
        {
            // Arrange
            var factory = new RecordingTimerFactory();

            // Act / Assert
            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => DeclarativeTimerBinder.BindTimersFromAttributes(block, factory), reason);
            StringAssert.Contains(refusal.Message, "empty identifier", "An unnamed timer reaches an operator's dashboard as a timer nobody can attribute to a method.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.2")]
        public void RefuseTwoTimersDeclaringOneIdentifier()
        {
            // Arrange
            var factory = new RecordingTimerFactory();

            // Act / Assert
            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => DeclarativeTimerBinder.BindTimersFromAttributes(new DuplicateIdentifierBlock(), factory));
            StringAssert.Contains(refusal.Message, nameof(DuplicateIdentifierBlock.First), "The refusal names both methods, because either one may be the mistake.");
            StringAssert.Contains(refusal.Message, nameof(DuplicateIdentifierBlock.Second));
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.3")]
        public void BindTimerDeclaredPrivateOnBaseClass()
        {
            // Arrange
            var factory = new RecordingTimerFactory();

            // Act
            DeclarativeTimerBinder.BindTimersFromAttributes(new DerivedTimerBlock(), factory);

            // Assert
            CollectionAssert.AreEquivalent(new[] { "base", "derived" },
                                           factory.Registered.Select(timer => timer.Identifier).ToArray(),
                                           "A base class that schedules its own cycle from a private method used to bind no timer and warn about nothing.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.3")]
        public void CountOverriddenTimerMethodOnce()
        {
            // Arrange
            var factory = new RecordingTimerFactory();

            // Act
            DeclarativeTimerBinder.BindTimersFromAttributes(new OverridingTimerBlock(), factory);

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(9),
                            factory.Registered.Single().Interval,
                            "A method and the method it overrides are one timer, and the most derived declaration is the one that binds.");
        }

        public static IEnumerable<object[]> BlankIdentifiers()
        {
            yield return [new EmptyIdentifierBlock(), "an identifier with nothing in it"];
            yield return [new WhitespaceIdentifierBlock(), "an identifier of whitespace"];
        }

        public static IEnumerable<object[]> UnschedulableIntervals()
        {
            yield return [new NotANumberIntervalBlock(), "an interval that is not a number"];
            yield return [new InfiniteIntervalBlock(), "an interval of infinity"];
            yield return [new OverlongIntervalBlock(), "an interval longer than a real clock can wait"];
            yield return [new SubTickIntervalBlock(), "an interval shorter than one clock tick"];
        }

        /// <summary>Captures what the binder registered, in the order it registered it.</summary>
        private sealed class RecordingTimerFactory : ITimerFactory
        {
            public List<(string Identifier, TimeSpan Interval, Action Callback)> Registered { get; } = [];

            public void RegisterTimer(string identifier, TimeSpan interval, Action callback)
            {
                Registered.Add((identifier, interval, callback));
            }
        }

        private sealed class NamedTimerBlock : LogicBlockBase
        {
            public NamedTimerBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            [Timer(2.5, "cycle")]
            private void Tick()
            {
            }
        }

        public sealed class UnnamedTimerBlock : LogicBlockBase
        {
            public UnnamedTimerBlock() : base(NullLogger.Instance)
            {
            }

            [Timer(2.5)]
            public void Tick()
            {
            }

            protected override void Ready()
            {
            }
        }

        private sealed class BadSignatureTimerBlock : LogicBlockBase
        {
            public BadSignatureTimerBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            // Deliberately the shape DALE002 reports, to prove the binder's own guard still fires for a block
            // that shipped past a suppressed diagnostic.
#pragma warning disable DALE002
            [Timer(1.0)]
            private int ReturnsSomething()
            {
                return 0;
            }
#pragma warning restore DALE002
        }

        public sealed class NotANumberIntervalBlock : LogicBlockBase
        {
            public NotANumberIntervalBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            // Deliberately the not-a-number interval DALE005 reports; the binder is the door left when it
            // is suppressed.
#pragma warning disable DALE005
            [Timer(double.NaN)]
            private void Tick()
            {
            }
#pragma warning restore DALE005
        }

        public sealed class InfiniteIntervalBlock : LogicBlockBase
        {
            public InfiniteIntervalBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            // Deliberately the infinite interval DALE005 reports; the binder is the door left when it
            // is suppressed.
#pragma warning disable DALE005
            [Timer(double.PositiveInfinity)]
            private void Tick()
            {
            }
#pragma warning restore DALE005
        }

        public sealed class OverlongIntervalBlock : LogicBlockBase
        {
            public OverlongIntervalBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            // Deliberately the longer-than-a-clock-can-wait interval DALE005 reports; the binder is the door left when it
            // is suppressed.
#pragma warning disable DALE005
            [Timer(4294968)]
            private void Tick()
            {
            }
#pragma warning restore DALE005
        }

        public sealed class SubTickIntervalBlock : LogicBlockBase
        {
            public SubTickIntervalBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            // Positive, so the attribute's own guard lets it past — and shorter than one clock tick, so it
            // converts to no delay at all and would arm a chain that never yields. Deliberately the
            // sub-tick interval DALE005 reports; the binder is the door left when it is suppressed.
#pragma warning disable DALE005
            [Timer(1e-9)]
            private void Tick()
            {
            }
#pragma warning restore DALE005
        }

        public sealed class EmptyIdentifierBlock : LogicBlockBase
        {
            public EmptyIdentifierBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            [Timer(1.0, "")]
            private void Tick()
            {
            }
        }

        public sealed class WhitespaceIdentifierBlock : LogicBlockBase
        {
            public WhitespaceIdentifierBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            [Timer(1.0, "   ")]
            private void Tick()
            {
            }
        }

        public sealed class DuplicateIdentifierBlock : LogicBlockBase
        {
            public DuplicateIdentifierBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            // Deliberately the shape DALE012 reports; the binder is the door left when it is suppressed.
#pragma warning disable DALE012
            [Timer(4.0, "shared")]
            public void First()
            {
            }

            [Timer(7.0, "shared")]
            public void Second()
            {
            }
#pragma warning restore DALE012
        }

        public class BaseTimerBlock : LogicBlockBase
        {
            public BaseTimerBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            [Timer(5.0, "base")]
            private void PrivateBaseTick()
            {
            }
        }

        public sealed class DerivedTimerBlock : BaseTimerBlock
        {
            [Timer(3.0, "derived")]
            private void DerivedTick()
            {
            }
        }

        public class VirtualTimerBlock : LogicBlockBase
        {
            public VirtualTimerBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            [Timer(4.0, "virtual")]
            protected virtual void Tick()
            {
            }
        }

        public sealed class OverridingTimerBlock : VirtualTimerBlock
        {
            [Timer(9.0, "virtual")]
            protected override void Tick()
            {
            }
        }
    }
}