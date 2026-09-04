using System;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Diagnostics
{
    [TestClass]
    public class ModbusTcpConnectionAccumulatorShould
    {
        private static readonly DateTime ObservedAt = new(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc);

        private readonly ModbusTcpConnectionAccumulator _sut = new();

        private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(ObservedAt));

        [TestInitialize]
        public void Initialize()
        {
            _sut.UseClock(_timeProvider);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-017.1")]
        public void ReportSocketAsDisconnectedBeforeAnyAttempt()
        {
            // Arrange

            // Act
            var summary = _sut.Snapshot();

            // Assert
            Assert.AreEqual(ModbusTcpConnectionState.Disconnected, summary.State);
            Assert.IsNull(summary.LastConnectedAt);
            Assert.AreEqual(0L, summary.ConnectAttemptCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-017.1")]
        public void ReportEveryAttemptAndHandshakeItTook()
        {
            // Arrange
            var handshake = TimeSpan.FromMilliseconds(40);

            // Act
            _sut.RecordConnectAttempt();
            _sut.RecordConnected(ObservedAt, handshake);

            // Assert
            var summary = _sut.Snapshot();
            Assert.AreEqual(ModbusTcpConnectionState.Connected, summary.State);
            Assert.AreEqual(1L, summary.ConnectAttemptCount);
            Assert.AreEqual(handshake, summary.LastConnectDuration);
            Assert.AreEqual(ObservedAt, summary.LastConnectedAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-017.2")]
        public void ClearConsecutiveRunOnSuccessfulConnectWithoutClearingTotals()
        {
            // Arrange
            _sut.RecordConnectAttempt();
            _sut.RecordConnectFailed(ObservedAt);
            _sut.RecordConnectAttempt();
            _sut.RecordConnectFailed(ObservedAt);

            // Act
            _sut.RecordConnectAttempt();
            _sut.RecordConnected(ObservedAt, TimeSpan.Zero);

            // Assert
            var summary = _sut.Snapshot();
            Assert.AreEqual(0, summary.ConsecutiveConnectFailures);
            Assert.AreEqual(2L, summary.ConnectFailureCount);
            Assert.AreEqual(3L, summary.ConnectAttemptCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-017.2")]
        public void ClearConsecutiveRunOnConfigurationChangeWithoutClearingTotals()
        {
            // Arrange
            _sut.RecordConnectAttempt();
            _sut.RecordConnectFailed(ObservedAt);
            _sut.RecordConnectAttempt();
            _sut.RecordConnectFailed(ObservedAt);

            // Act
            _sut.ResetConnectBackoff();

            // Assert
            var summary = _sut.Snapshot();
            Assert.AreEqual(0, summary.ConsecutiveConnectFailures);
            Assert.AreEqual(2L, summary.ConnectFailureCount);
            Assert.IsNull(summary.CurrentBackoff);
            Assert.IsNull(summary.NextAttemptAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-017.3")]
        public void ReportBackingOffOnlyUntilArmedInstantPasses()
        {
            // Arrange
            var backoff = TimeSpan.FromSeconds(5);
            _sut.RecordConnectAttempt();
            _sut.RecordConnectFailed(ObservedAt);
            _sut.RecordConnectBackoff(backoff, ObservedAt + backoff);

            // Act
            var whileWaiting = _sut.Snapshot();
            _timeProvider.Advance(backoff);
            var afterWaiting = _sut.Snapshot();

            // Assert
            Assert.AreEqual(ModbusTcpConnectionState.BackingOff, whileWaiting.State);
            Assert.AreEqual(ModbusTcpConnectionState.Disconnected, afterWaiting.State);
            Assert.AreEqual(backoff, afterWaiting.CurrentBackoff);
            Assert.AreEqual(ObservedAt + backoff, afterWaiting.NextAttemptAt);
        }
    }
}
