using Vion.Dale.Sdk.DigitalIo.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Examples.PingPong.LogicBlocks;
using Xunit;

namespace Vion.Examples.PingPong.Test
{
    public class PingShould
    {
        private readonly InterfaceId _mappedPong = new("pong-id", "IPong");

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DigitalInput_Changed_LogValue(bool value)
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var ping = new Ping(loggerMock.Object);
            ping.InitializeForTest();

            // Act
            ping.DigitalInput.RaiseInputChanged(value);

            // Assert
            loggerMock.VerifyLogContains("DI changed to", LogLevel.Information, Times.Once());
        }

        [Fact]
        public void HandlePongResponse_PauseIsFalse_SendPingRequest()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var ping = new Ping(loggerMock.Object);
            var testContext = ping.CreateTestContext().WithLogicInterfaceMapping(lb => lb, _mappedPong).Build();

            ping.Pause = false;
            testContext.ClearRecordedMessages();

            // Act
            ping.HandleResponse(_mappedPong, new Contracts.PingPong.PongResponse());

            // Assert
            testContext.VerifySendRequest<Contracts.PingPong.PingRequest>(_mappedPong);
        }

        [Fact]
        public void HandlePongResponse_PauseIsTrue_NotSendPingRequest()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var ping = new Ping(loggerMock.Object);
            var testContext = ping.CreateTestContext().WithLogicInterfaceMapping(lb => lb, _mappedPong).Build();

            ping.Pause = true;
            testContext.ClearRecordedMessages();

            // Act
            ping.HandleResponse(_mappedPong, new Contracts.PingPong.PongResponse());

            // Assert
            testContext.VerifySendRequest<Contracts.PingPong.PingRequest>(times: Times.Never());
        }

        [Fact]
        public void LogCount_AfterPing_SetsPingsPerSecond()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var ping = new Ping(loggerMock.Object);
            ping.CreateTestContext().WithLogicInterfaceMapping(lb => lb, _mappedPong).WithoutAutoStart().Build();

            // Trigger one ping via HandleResponse (Pause is false by default)
            ping.HandleResponse(_mappedPong, new Contracts.PingPong.PongResponse());

            // Act
            ping.LogCount();

            // Assert
            Assert.Equal(1, ping.PingsPerSecond);
            loggerMock.VerifyLogContains("messages pinged back since last log", LogLevel.Information, Times.Once());
        }

        [Fact]
        public void LogCount_NoPings_SetsPingsPerSecondToZero()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var ping = new Ping(loggerMock.Object);
            ping.InitializeForTest();

            // Act
            ping.LogCount();

            // Assert
            Assert.Equal(0, ping.PingsPerSecond);
            loggerMock.VerifyLogContains("messages pinged back since last log", LogLevel.Information, Times.Once());
        }

        [Fact]
        public void PauseSet_ToFalse_SendPingRequest()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var ping = new Ping(loggerMock.Object);
            var testContext = ping.CreateTestContext().WithLogicInterfaceMapping(lb => lb, _mappedPong).Build();

            ping.Pause = true;
            testContext.ClearRecordedMessages();

            // Act
            ping.Pause = false;

            // Assert
            testContext.VerifySendRequest<Contracts.PingPong.PingRequest>(_mappedPong);
        }

        [Fact]
        public void PauseSet_ToTrue_NotSendPingRequest()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var ping = new Ping(loggerMock.Object);
            var testContext = ping.CreateTestContext().WithLogicInterfaceMapping(lb => lb, _mappedPong).Build();
            ping.Pause = false;
            testContext.ClearRecordedMessages();

            // Act
            ping.Pause = true;

            // Assert
            testContext.VerifySendRequest<Contracts.PingPong.PingRequest>(times: Times.Never());
        }
    }
}