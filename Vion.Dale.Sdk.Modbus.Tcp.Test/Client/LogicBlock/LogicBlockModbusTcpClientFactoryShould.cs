using System;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;
using Moq;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Client.LogicBlock
{
    [TestClass]
    public class LogicBlockModbusTcpClientFactoryShould
    {
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();

        private LogicBlockModbusTcpClientFactory _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = new LogicBlockModbusTcpClientFactory(_serviceProviderMock.Object);
        }

        [TestMethod]
        public void CreateLogicBlockModbusTcpClientInstance()
        {
            // Arrange
            var expectedClient = new Mock<ILogicBlockModbusTcpClient>().Object;
            _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogicBlockModbusTcpClient))).Returns(expectedClient);

            // Act
            var actualClient = _sut.Create();

            // Assert
            Assert.IsNotNull(actualClient);
            Assert.AreSame(expectedClient, actualClient);
        }
    }
}