using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     Pins the premise <c>docs/masking-published-values.md</c> rests on: a service member written as a getter
    ///     over a private backing field and a private <c>bool</c> flag re-publishes when either input changes.
    ///     Publication is decided by <c>PropertyChanged</c> carrying the member's name, so that event is what
    ///     every test here observes.
    ///     <para>
    ///         A premise test: it cites no spec id, because what it holds in place is the weaver's dependency
    ///         tracking for one getter shape and not a criterion of the SDK's own.
    ///     </para>
    /// </summary>
    [TestClass]
    public class MaskedGetterShould
    {
        [TestMethod]
        [DataRow(nameof(MaskedGetterBlock.Power), true, false)]
        [DataRow(nameof(MaskedGetterBlock.Reading), true, false)]
        [DataRow(nameof(MaskedGetterBlock.Current), false, true)]
        public void RaisePropertyChangedWhenWindowFlagRaised(string member, bool powerWindowLive, bool voltageWindowLive)
        {
            // Arrange
            var sut = new MaskedGetterBlock();
            sut.Store(1.5);
            var raised = Subscribe(sut);

            // Act
            sut.SetWindows(powerWindowLive, voltageWindowLive);

            // Assert
            CollectionAssert.Contains(raised, member);
        }

        [TestMethod]
        [DataRow(nameof(MaskedGetterBlock.Power), true, false)]
        [DataRow(nameof(MaskedGetterBlock.Reading), true, false)]
        [DataRow(nameof(MaskedGetterBlock.Current), false, true)]
        public void RaisePropertyChangedWhenWindowFlagLowered(string member, bool powerWindowLive, bool voltageWindowLive)
        {
            // Arrange
            var sut = new MaskedGetterBlock();
            sut.Store(1.5);
            sut.SetWindows(powerWindowLive, voltageWindowLive);
            var raised = Subscribe(sut);

            // Act
            sut.SetWindows(false, false);

            // Assert
            CollectionAssert.Contains(raised, member);
        }

        [TestMethod]
        [DataRow(nameof(MaskedGetterBlock.Power))]
        [DataRow(nameof(MaskedGetterBlock.Reading))]
        [DataRow(nameof(MaskedGetterBlock.Current))]
        public void RaisePropertyChangedWhenBackingAssignedWhileWindowsLive(string member)
        {
            // Arrange
            var sut = new MaskedGetterBlock();
            sut.Store(1.5);
            sut.SetWindows(true, true);
            var raised = Subscribe(sut);

            // Act
            sut.Store(2.5);

            // Assert
            CollectionAssert.Contains(raised, member);
        }

        [TestMethod]
        public void RaisePropertyChangedWhenSecondWindowFlagRaised()
        {
            // Arrange
            var sut = new MaskedGetterBlock();
            sut.Store(1.5);
            sut.SetWindows(false, true);
            var raised = Subscribe(sut);

            // Act
            sut.SetWindows(true, true);

            // Assert
            CollectionAssert.Contains(raised, nameof(MaskedGetterBlock.Current));
        }

        private static List<string> Subscribe(MaskedGetterBlock sut)
        {
            var raised = new List<string>();
            ((INotifyPropertyChanged)sut).PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "<null>");
            return raised;
        }

        public readonly record struct MeterReading(double ActivePowerTotalKw);

        private sealed class MaskedGetterBlock : LogicBlockBase
        {
            private double? _magnitude;

            private double? _power;

            private bool _powerWindowLive;

            private MeterReading? _reading;

            private bool _voltageWindowLive;

            [ServiceProperty]
            [ServiceMeasuringPoint]
            public double? Power
            {
                get => _powerWindowLive ? _power : null;
            }

            [ServiceProperty]
            public MeterReading? Reading
            {
                get => _powerWindowLive ? _reading : null;
            }

            // Reads two windows: the signed value once both are live, the bare magnitude while only its own is.
            [ServiceProperty]
            public double? Current
            {
                get => _voltageWindowLive ? _powerWindowLive ? _power : _magnitude : null;
            }

            public MaskedGetterBlock() : base(new Mock<ILogger>().Object)
            {
            }

            public void Store(double value)
            {
                _power = value;
                _magnitude = value + 1;
                _reading = new MeterReading(value);
            }

            public void SetWindows(bool powerWindowLive, bool voltageWindowLive)
            {
                _powerWindowLive = powerWindowLive;
                _voltageWindowLive = voltageWindowLive;
            }

            protected override void Ready()
            {
            }
        }
    }
}
