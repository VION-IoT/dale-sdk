using Vion.Dale.Sdk.DigitalIo.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Examples.PingPong.Contracts;
using Vion.Examples.PingPong.LogicBlocks;
using Xunit;

namespace Vion.Examples.PingPong.Test
{
    public class PongShould
    {
        private readonly InterfaceId _mappedPing = new("ping-id", "IPing");

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DigitalOutput_Changed_LogValue(bool value)
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var pong = new Pong(loggerMock.Object);
            pong.InitializeForTest();

            // Act
            pong.DigitalOutput.RaiseOutputChanged(value);

            // Assert
            loggerMock.VerifyLogContains("DO changed to", LogLevel.Information, Times.Once());
        }

        [Fact]
        public void HandlePingRequest_Returns_PongResponse()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var pong = new Pong(loggerMock.Object);
            pong.InitializeForTest();

            // Act
            var response = pong.HandleRequest(new Contracts.PingPong.PingRequest());

            // Assert
            Assert.Equal(new Contracts.PingPong.PongResponse(), response);
        }

        [Fact]
        public void LogCount_AfterHandleRequest_SetsPongsPerSecond()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var pong = new Pong(loggerMock.Object);
            pong.InitializeForTest();

            pong.HandleRequest(new Contracts.PingPong.PingRequest());

            // Act
            pong.LogCount();

            // Assert
            Assert.Equal(1, pong.PongsPerSecond);
            loggerMock.VerifyLogContains("messages pinged back since last log", LogLevel.Debug, Times.Once());
        }

        [Fact]
        public void LogCount_NoPongs_SetsPongsPerSecondToZero()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var pong = new Pong(loggerMock.Object);
            pong.InitializeForTest();

            // Act
            pong.LogCount();

            // Assert
            Assert.Equal(0, pong.PongsPerSecond);
            loggerMock.VerifyLogContains("messages pinged back since last log", LogLevel.Debug, Times.Once());
        }

        [Fact]
        public void ToggleDo_WhenCalled_SetDigitalOutputValue()
        {
            // Arrange
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            var pong = new Pong(loggerMock.Object);
            var testContext = pong.CreateTestContext().WithLogicInterfaceMapping<IPong>(_mappedPing).Build();

            // Act
            pong.ToggleDo();

            // Assert
            testContext.VerifyDigitalOutputSet(pong.DigitalOutput, true);
        }
    }
}