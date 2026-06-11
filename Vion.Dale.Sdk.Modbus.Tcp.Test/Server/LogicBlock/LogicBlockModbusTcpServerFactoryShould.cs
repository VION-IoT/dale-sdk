using System;
using Moq;
using Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Server.LogicBlock
{
    [TestClass]
    public class LogicBlockModbusTcpServerFactoryShould
    {
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();

        private LogicBlockModbusTcpServerFactory _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = new LogicBlockModbusTcpServerFactory(_serviceProviderMock.Object);
        }

        [TestMethod]
        public void CreateLogicBlockModbusTcpServerInstance()
        {
            // Arrange
            var expectedServer = new Mock<ILogicBlockModbusTcpServer>().Object;
            _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILogicBlockModbusTcpServer))).Returns(expectedServer);

            // Act
            var actualServer = _sut.Create();

            // Assert
            Assert.IsNotNull(actualServer);
            Assert.AreSame(expectedServer, actualServer);
        }
    }
}
