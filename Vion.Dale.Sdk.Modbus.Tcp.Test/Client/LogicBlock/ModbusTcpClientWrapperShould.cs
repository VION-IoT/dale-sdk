using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Microsoft.Extensions.Logging;
using Moq;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Client.LogicBlock
{
    [TestClass]
    [SuppressMessage("Usage", "MSTEST0049:Flow TestContext.CancellationToken to async operations")]
    public class ModbusTcpClientWrapperShould
    {
        private const ushort Quantity = 12;

        private const ushort StartingAddress = 10;

        private const int UnitIdentifier = 42;

        private const ByteOrder ByteOrder = Vion.Dale.Sdk.Modbus.Core.Conversion.ByteOrder.LsbToMsb;

        private const WordOrder32 WordOrder32 = Vion.Dale.Sdk.Modbus.Core.Conversion.WordOrder32.LswToMsw;

        private const WordOrder64 WordOrder64 = Vion.Dale.Sdk.Modbus.Core.Conversion.WordOrder64.BADC;

        private const uint Count = 3;

        private const int BytesPer16BitValue = 2;

        private const int BytesPer32BitValue = 4;

        private const int BytesPer64BitValue = 8;

        // Sample target methods to test common functionality
        public enum TargetMethod
        {
            ReadDiscreteInputsAsync,

            ReadCoilsAsync,

            WriteSingleCoilAsync,

            WriteMultipleCoilsAsync,

            ReadInputRegistersAsFloatAsync,

            ReadHoldingRegistersAsIntAsync,

            WriteMultipleHoldingRegistersAsDoubleAsync,
        }

        private readonly CancellationToken _cancellationToken = CancellationToken.None;

        private readonly Mock<IModbusTcpClientProxy> _clientProxyMock = new();

        private readonly Mock<IModbusDataConverter> _dataConverterMock = new();

        private readonly Mock<ILogger<ModbusTcpClientWrapper>> _loggerMock = new();

        private readonly TimeSpan _operationTimeout = TimeSpan.FromSeconds(60);

        private readonly byte[] _registerBytes = [0x22, 0xB2, 0xC3, 0xB4];

        private readonly Mock<IModbusValidator> _validatorMock = new();

        private ModbusTcpClientWrapper _sut = null!;

        [TestInitialize]
        public async Task InitializeAsync()
        {
            _sut = new ModbusTcpClientWrapper(_clientProxyMock.Object, _validatorMock.Object, _dataConverterMock.Object, _loggerMock.Object);

            // Pre-connect to establish baseline connection state.
            // This ensures tests only detect reconnection when settings change during the test, not from the initial IpAddress assignment.
            _sut.IpAddress = IPAddress.Loopback;
            await _sut.ReadInputRegistersRawAsync(UnitIdentifier, StartingAddress, Quantity, _operationTimeout, _cancellationToken);
            _clientProxyMock.Invocations.Clear();
            _validatorMock.Invocations.Clear();
            _dataConverterMock.Invocations.Clear();
        }

        [TestMethod]
        public async Task DisconnectWhenConnected()
        {
            // Arrange
            _clientProxyMock.Setup(clientProxy => clientProxy.IsConnected).Returns(true);

            // Act
            await _sut.DisconnectAsync(_cancellationToken);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.Disconnect(), Times.Once);
        }

        [TestMethod]
        public async Task SkipDisconnectWhenNotConnected()
        {
            // Arrange
            _clientProxyMock.Setup(clientProxy => clientProxy.IsConnected).Returns(false);

            // Act
            await _sut.DisconnectAsync(_cancellationToken);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.Disconnect(), Times.Never);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task ThrowExceptionWhenIpAddressNotSet(TargetMethod targetMethod)
        {
            // Arrange
            _clientProxyMock.Setup(clientProxy => clientProxy.IsConnected).Returns(false);
            _sut.IpAddress = null;

            // Act / Assert
            await Assert.ThrowsAsync<IpAddressNotSetException>(() => InvokeMethodAsync(targetMethod));
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task ReconnectWhenPortChanged(TargetMethod targetMethod)
        {
            // Arrange
            _clientProxyMock.Setup(clientProxy => clientProxy.IsConnected).Returns(true);
            _sut.Port = 1502;

            // Act
            await InvokeMethodAsync(targetMethod);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                                    Times.Once);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task ReconnectWhenIpAddressChanged(TargetMethod targetMethod)
        {
            // Arrange
            _clientProxyMock.Setup(clientProxy => clientProxy.IsConnected).Returns(true);
            _sut.IpAddress = IPAddress.Parse("192.0.0.1");

            // Act
            await InvokeMethodAsync(targetMethod);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                                    Times.Once);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task ConnectWhenNotConnected(TargetMethod targetMethod)
        {
            // Arrange
            _clientProxyMock.Setup(clientProxy => clientProxy.IsConnected).Returns(false);

            // Act
            await InvokeMethodAsync(targetMethod);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                                    Times.Once);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task NotReconnectWhenConnectionSettingsUnchanged(TargetMethod targetMethod)
        {
            // Arrange
            _clientProxyMock.Setup(clientProxy => clientProxy.IsConnected).Returns(true);

            // Act
            await InvokeMethodAsync(targetMethod);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                                    Times.Never);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task DisconnectBeforeReconnecting(TargetMethod targetMethod)
        {
            // Arrange
            var callOrder = new List<string>();
            _clientProxyMock.Setup(clientProxy => clientProxy.IsConnected).Returns(true);
            _clientProxyMock.Setup(clientProxy => clientProxy.Disconnect()).Callback(() => callOrder.Add("Disconnect"));
            _clientProxyMock.Setup(clientProxy => clientProxy.ConnectAsync(It.IsAny<IPAddress>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                            .Callback(() => callOrder.Add("Connect"));
            _sut.Port = 1502;

            // Act
            await InvokeMethodAsync(targetMethod);

            // Assert
            Assert.HasCount(2, callOrder);
            Assert.AreEqual("Disconnect", callOrder[0]);
            Assert.AreEqual("Connect", callOrder[1]);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task ConnectWithConfiguredSettings(TargetMethod targetMethod)
        {
            // Arrange
            var ipAddress = _sut.IpAddress = IPAddress.Parse("192.0.0.1");
            var port = _sut.Port = 1502;
            var connectionTimeout = _sut.ConnectionTimeout = TimeSpan.FromSeconds(2);

            // Act
            await InvokeMethodAsync(targetMethod);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.ConnectAsync(ipAddress, port, connectionTimeout, It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task ValidateUnitIdentifier(TargetMethod targetMethod)
        {
            // Arrange

            // Act
            await InvokeMethodAsync(targetMethod);

            // Assert
            _validatorMock.Verify(validator => validator.ValidateUnitIdentifier(UnitIdentifier), Times.Once);
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task ThrowExceptionWhenUnitIdentifierInvalid(TargetMethod targetMethod)
        {
            // Arrange
            _validatorMock.Setup(validator => validator.ValidateUnitIdentifier(It.IsAny<int>())).Callback(() => throw new InvalidUnitIdentifierException(1));

            // Act / Assert
            await Assert.ThrowsAsync<InvalidUnitIdentifierException>(() => InvokeMethodAsync(targetMethod));
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task ThrowExceptionOnTimeout(TargetMethod targetMethod)
        {
            // Arrange
            SetupReturns(targetMethod);

            // Act / Assert
            await Assert.ThrowsAsync<OperationTimeoutException>(() => InvokeMethodAsync(targetMethod, TimeSpan.FromSeconds(0)));
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task ThrowExceptionOnCancellation(TargetMethod targetMethod)
        {
            // Arrange
            SetupReturns(targetMethod);
            var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            // Act / Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() => InvokeMethodAsync(targetMethod, Timeout.InfiniteTimeSpan, cts.Token));
        }

        [TestMethod]
        [DataRow(TargetMethod.ReadDiscreteInputsAsync)]
        [DataRow(TargetMethod.ReadCoilsAsync)]
        [DataRow(TargetMethod.WriteSingleCoilAsync)]
        [DataRow(TargetMethod.WriteMultipleCoilsAsync)]
        [DataRow(TargetMethod.ReadInputRegistersAsFloatAsync)]
        [DataRow(TargetMethod.ReadHoldingRegistersAsIntAsync)]
        [DataRow(TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync)]
        public async Task PropagateProxyException(TargetMethod targetMethod)
        {
            // Arrange
            SetupThrows(targetMethod, new TestException());

            // Act / Assert
            await Assert.ThrowsAsync<TestException>(() => InvokeMethodAsync(targetMethod));
        }

        [TestMethod]
        public async Task ReadDiscreteInputs()
        {
            // Arrange
            bool[] expectedBools = [true, false, true];
            SetupConvertBitsToBoolsReturns(expectedBools);
            _clientProxyMock.Setup(clientProxy => clientProxy.ReadDiscreteInputsAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                            .ReturnsAsync(_registerBytes);

            // Act
            var actualBools = await _sut.ReadDiscreteInputsAsync(UnitIdentifier, StartingAddress, Quantity, _operationTimeout, _cancellationToken);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.ReadDiscreteInputsAsync(UnitIdentifier, StartingAddress, Quantity, It.IsAny<CancellationToken>()), Times.Once);
            VerifyConvertBitsToBoolsInvoked();
            CollectionAssert.AreEqual(expectedBools, actualBools);
        }

        [TestMethod]
        public async Task ReadCoils()
        {
            // Arrange
            bool[] expectedBools = [true, false, true];
            SetupConvertBitsToBoolsReturns(expectedBools);
            _clientProxyMock.Setup(clientProxy => clientProxy.ReadCoilsAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                            .ReturnsAsync(_registerBytes);

            // Act
            var actualBools = await _sut.ReadCoilsAsync(UnitIdentifier, StartingAddress, Quantity, _operationTimeout, _cancellationToken);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.ReadCoilsAsync(UnitIdentifier, StartingAddress, Quantity, It.IsAny<CancellationToken>()), Times.Once);
            VerifyConvertBitsToBoolsInvoked();
            CollectionAssert.AreEqual(expectedBools, actualBools);
        }

        [TestMethod]
        public async Task WriteSingleCoil()
        {
            // Arrange
            const bool value = true;

            // Act
            await _sut.WriteSingleCoilAsync(UnitIdentifier, StartingAddress, value, _operationTimeout, _cancellationToken);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.WriteSingleCoilAsync(UnitIdentifier, StartingAddress, value, It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task WriteMultipleCoils()
        {
            // Arrange
            bool[] values = [true, false, true, true];

            // Act
            await _sut.WriteMultipleCoilsAsync(UnitIdentifier, StartingAddress, values, _operationTimeout, _cancellationToken);

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.WriteMultipleCoilsAsync(UnitIdentifier, StartingAddress, values, It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task ReadInputRegistersRaw()
        {
            // Arrange
            SetupReadInputRegistersReturns();

            // Act
            var actualBytes = await _sut.ReadInputRegistersRawAsync(UnitIdentifier, StartingAddress, Quantity, _operationTimeout, _cancellationToken);

            // Assert
            VerifyReadInputRegistersInvoked();
            CollectionAssert.AreEqual(_registerBytes.ToArray(), actualBytes);
        }

        [TestMethod]
        public async Task ReadInputRegistersAsShort()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadInputRegistersAsShortAsync(UnitIdentifier,
                                                                                   StartingAddress,
                                                                                   Quantity,
                                                                                   ByteOrder,
                                                                                   _operationTimeout,
                                                                                   _cancellationToken),
                                         new short[] { 1, 2 },
                                         BytesPer16BitValue,
                                         VerifyReadInputRegistersInvoked);
        }

        [TestMethod]
        public async Task ReadInputRegistersAsUShort()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadInputRegistersAsUShortAsync(UnitIdentifier,
                                                                                    StartingAddress,
                                                                                    Quantity,
                                                                                    ByteOrder,
                                                                                    _operationTimeout,
                                                                                    _cancellationToken),
                                         new ushort[] { 1, 2 },
                                         BytesPer16BitValue,
                                         VerifyReadInputRegistersInvoked);
        }

        [TestMethod]
        public async Task ReadInputRegistersAsInt()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadInputRegistersAsIntAsync(UnitIdentifier,
                                                                                 StartingAddress,
                                                                                 Count,
                                                                                 ByteOrder,
                                                                                 WordOrder32,
                                                                                 _operationTimeout,
                                                                                 _cancellationToken),
                                         [1, 2],
                                         BytesPer32BitValue,
                                         () =>
                                         {
                                             VerifyReadInputRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
                                             VerifySwapWords32Invoked();
                                         });
        }

        [TestMethod]
        public async Task ReadInputRegistersAsUInt()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadInputRegistersAsUIntAsync(UnitIdentifier,
                                                                                  StartingAddress,
                                                                                  Count,
                                                                                  ByteOrder,
                                                                                  WordOrder32,
                                                                                  _operationTimeout,
                                                                                  _cancellationToken),
                                         [1U, 2U],
                                         BytesPer32BitValue,
                                         () =>
                                         {
                                             VerifyReadInputRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
                                             VerifySwapWords32Invoked();
                                         });

            // Assert
            VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
        }

        [TestMethod]
        public async Task ReadInputRegistersAsFloat()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadInputRegistersAsFloatAsync(UnitIdentifier,
                                                                                   StartingAddress,
                                                                                   Count,
                                                                                   ByteOrder,
                                                                                   WordOrder32,
                                                                                   _operationTimeout,
                                                                                   _cancellationToken),
                                         [1, 2],
                                         BytesPer32BitValue,
                                         () =>
                                         {
                                             VerifyReadInputRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
                                             VerifySwapWords32Invoked();
                                         });
        }

        [TestMethod]
        public async Task ReadInputRegistersAsLong()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadInputRegistersAsLongAsync(UnitIdentifier,
                                                                                  StartingAddress,
                                                                                  Count,
                                                                                  ByteOrder,
                                                                                  WordOrder64,
                                                                                  _operationTimeout,
                                                                                  _cancellationToken),
                                         [1, 2],
                                         BytesPer64BitValue,
                                         () =>
                                         {
                                             VerifyReadInputRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
                                             VerifySwapWords64Invoked();
                                         });
        }

        [TestMethod]
        public async Task ReadInputRegistersAsULong()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadInputRegistersAsULongAsync(UnitIdentifier,
                                                                                   StartingAddress,
                                                                                   Count,
                                                                                   ByteOrder,
                                                                                   WordOrder64,
                                                                                   _operationTimeout,
                                                                                   _cancellationToken),
                                         [1UL, 2UL],
                                         BytesPer64BitValue,
                                         () =>
                                         {
                                             VerifyReadInputRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
                                             VerifySwapWords64Invoked();
                                         });
        }

        [TestMethod]
        public async Task ReadInputRegistersAsDouble()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadInputRegistersAsDoubleAsync(UnitIdentifier,
                                                                                    StartingAddress,
                                                                                    Count,
                                                                                    ByteOrder,
                                                                                    WordOrder64,
                                                                                    _operationTimeout,
                                                                                    _cancellationToken),
                                         [1, 2],
                                         BytesPer64BitValue,
                                         () =>
                                         {
                                             VerifyReadInputRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
                                             VerifySwapWords64Invoked();
                                         });
        }

        [TestMethod]
        [DataRow(TextEncoding.Ascii)]
        [DataRow(TextEncoding.Utf8)]
        [DataRow(TextEncoding.Utf16Be)]
        [DataRow(TextEncoding.Utf16Le)]
        public async Task ReadInputRegistersAsString(TextEncoding textEncoding)
        {
            // Arrange
            SetupReadInputRegistersReturns();
            var expectedString = Guid.NewGuid().ToString();
            _dataConverterMock.Setup(converter => converter.ConvertBytesToString(It.IsAny<Memory<byte>>(), It.IsAny<TextEncoding>())).Returns(expectedString);

            // Act
            var actualString = await _sut.ReadInputRegistersAsStringAsync(UnitIdentifier,
                                                                          StartingAddress,
                                                                          Quantity,
                                                                          textEncoding,
                                                                          _operationTimeout,
                                                                          _cancellationToken);

            // Assert
            _dataConverterMock.Verify(dataConverter =>
                                          dataConverter.ConvertBytesToString(It.Is<Memory<byte>>(buffer => buffer.ToArray().AsEnumerable().SequenceEqual(_registerBytes)),
                                                                             textEncoding),
                                      Times.Once);
            Assert.AreEqual(expectedString, actualString);
        }

        [TestMethod]
        public async Task ReadHoldingRegistersRaw()
        {
            // Arrange
            SetupReadHoldingRegistersReturns();

            // Act
            var actualBytes = await _sut.ReadHoldingRegistersRawAsync(UnitIdentifier, StartingAddress, Quantity, _operationTimeout, _cancellationToken);

            // Assert
            VerifyReadHoldingRegistersInvoked();
            CollectionAssert.AreEqual(_registerBytes.ToArray(), actualBytes);
        }

        [TestMethod]
        public async Task ReadHoldingRegistersAsShort()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadHoldingRegistersAsShortAsync(UnitIdentifier,
                                                                                     StartingAddress,
                                                                                     Quantity,
                                                                                     ByteOrder,
                                                                                     _operationTimeout,
                                                                                     _cancellationToken),
                                         new short[] { 1, 2 },
                                         BytesPer16BitValue,
                                         VerifyReadHoldingRegistersInvoked);
        }

        [TestMethod]
        public async Task ReadHoldingRegistersAsUShort()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadHoldingRegistersAsUShortAsync(UnitIdentifier,
                                                                                      StartingAddress,
                                                                                      Quantity,
                                                                                      ByteOrder,
                                                                                      _operationTimeout,
                                                                                      _cancellationToken),
                                         new ushort[] { 1, 2 },
                                         BytesPer16BitValue,
                                         VerifyReadHoldingRegistersInvoked);
        }

        [TestMethod]
        public async Task ReadHoldingRegistersAsInt()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadHoldingRegistersAsIntAsync(UnitIdentifier,
                                                                                   StartingAddress,
                                                                                   Count,
                                                                                   ByteOrder,
                                                                                   WordOrder32,
                                                                                   _operationTimeout,
                                                                                   _cancellationToken),
                                         [1, 2],
                                         BytesPer32BitValue,
                                         () =>
                                         {
                                             VerifyReadHoldingRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
                                             VerifySwapWords32Invoked();
                                         });
        }

        [TestMethod]
        public async Task ReadHoldingRegistersAsUInt()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadHoldingRegistersAsUIntAsync(UnitIdentifier,
                                                                                    StartingAddress,
                                                                                    Count,
                                                                                    ByteOrder,
                                                                                    WordOrder32,
                                                                                    _operationTimeout,
                                                                                    _cancellationToken),
                                         [1U, 2U],
                                         BytesPer32BitValue,
                                         () =>
                                         {
                                             VerifyReadHoldingRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
                                             VerifySwapWords32Invoked();
                                         });

            // Assert
            VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
        }

        [TestMethod]
        public async Task ReadHoldingRegistersAsFloat()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadHoldingRegistersAsFloatAsync(UnitIdentifier,
                                                                                     StartingAddress,
                                                                                     Count,
                                                                                     ByteOrder,
                                                                                     WordOrder32,
                                                                                     _operationTimeout,
                                                                                     _cancellationToken),
                                         [1, 2],
                                         BytesPer32BitValue,
                                         () =>
                                         {
                                             VerifyReadHoldingRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer32BitValue);
                                             VerifySwapWords32Invoked();
                                         });
        }

        [TestMethod]
        public async Task ReadHoldingRegistersAsLong()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadHoldingRegistersAsLongAsync(UnitIdentifier,
                                                                                    StartingAddress,
                                                                                    Count,
                                                                                    ByteOrder,
                                                                                    WordOrder64,
                                                                                    _operationTimeout,
                                                                                    _cancellationToken),
                                         [1, 2],
                                         BytesPer64BitValue,
                                         () =>
                                         {
                                             VerifyReadHoldingRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
                                             VerifySwapWords64Invoked();
                                         });
        }

        [TestMethod]
        public async Task ReadHoldingRegistersAsULong()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadHoldingRegistersAsULongAsync(UnitIdentifier,
                                                                                     StartingAddress,
                                                                                     Count,
                                                                                     ByteOrder,
                                                                                     WordOrder64,
                                                                                     _operationTimeout,
                                                                                     _cancellationToken),
                                         [1UL, 2UL],
                                         BytesPer64BitValue,
                                         () =>
                                         {
                                             VerifyReadHoldingRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
                                             VerifySwapWords64Invoked();
                                         });
        }

        [TestMethod]
        public async Task ReadHoldingRegistersAsDouble()
        {
            await ReadRegistersCoreAsync(() => _sut.ReadHoldingRegistersAsDoubleAsync(UnitIdentifier,
                                                                                      StartingAddress,
                                                                                      Count,
                                                                                      ByteOrder,
                                                                                      WordOrder64,
                                                                                      _operationTimeout,
                                                                                      _cancellationToken),
                                         [1, 2],
                                         BytesPer64BitValue,
                                         () =>
                                         {
                                             VerifyReadHoldingRegistersInvoked();
                                             VerifyConvertCountToQuantityInvoked(BytesPer64BitValue);
                                             VerifySwapWords64Invoked();
                                         });
        }

        [TestMethod]
        [DataRow(TextEncoding.Ascii)]
        [DataRow(TextEncoding.Utf8)]
        [DataRow(TextEncoding.Utf16Be)]
        [DataRow(TextEncoding.Utf16Le)]
        public async Task ReadHoldingRegistersAsString(TextEncoding textEncoding)
        {
            // Arrange
            SetupReadHoldingRegistersReturns();
            var expectedString = Guid.NewGuid().ToString();
            _dataConverterMock.Setup(converter => converter.ConvertBytesToString(It.IsAny<Memory<byte>>(), It.IsAny<TextEncoding>())).Returns(expectedString);

            // Act
            var actualString = await _sut.ReadHoldingRegistersAsStringAsync(UnitIdentifier,
                                                                            StartingAddress,
                                                                            Quantity,
                                                                            textEncoding,
                                                                            _operationTimeout,
                                                                            _cancellationToken);

            // Assert
            _dataConverterMock.Verify(dataConverter =>
                                          dataConverter.ConvertBytesToString(It.Is<Memory<byte>>(buffer => buffer.ToArray().AsEnumerable().SequenceEqual(_registerBytes)),
                                                                             textEncoding),
                                      Times.Once);
            Assert.AreEqual(expectedString, actualString);
        }

        [TestMethod]
        public async Task WriteSingleHoldingRegisterAsShort()
        {
            // Arrange
            SetupGetBytesReturns();
            const short value = 42;

            // Act
            await _sut.WriteSingleHoldingRegisterAsync(UnitIdentifier,
                                                       StartingAddress,
                                                       value,
                                                       ByteOrder,
                                                       _operationTimeout,
                                                       _cancellationToken);

            // Assert
            _dataConverterMock.Verify(dataConverter => dataConverter.GetBytes(value), Times.Once);
            VerifySwapBytesInvoked();
            VerifyWriteSingleRegisterInvoked();
        }

        [TestMethod]
        public async Task WriteSingleHoldingRegisterAsUShort()
        {
            // Arrange
            SetupGetBytesReturns();
            const ushort value = 42;

            // Act
            await _sut.WriteSingleHoldingRegisterAsync(UnitIdentifier,
                                                       StartingAddress,
                                                       value,
                                                       ByteOrder,
                                                       _operationTimeout,
                                                       _cancellationToken);

            // Assert
            _dataConverterMock.Verify(dataConverter => dataConverter.GetBytes(value), Times.Once);
            VerifySwapBytesInvoked();
            VerifyWriteSingleRegisterInvoked();
        }

        [TestMethod]
        public async Task WriteMultipleHoldingRegistersRaw()
        {
            // Arrange

            // Act
            await _sut.WriteMultipleHoldingRegistersRawAsync(UnitIdentifier, StartingAddress, _registerBytes, _operationTimeout, _cancellationToken);

            // Assert
            VerifyWriteMultipleRegistersInvoked();
        }

        [TestMethod]
        public async Task WriteMultipleHoldingRegistersAsShort()
        {
            await WriteHoldingRegistersCoreAsync(values => _sut.WriteMultipleHoldingRegistersAsShortAsync(UnitIdentifier,
                                                                                                          StartingAddress,
                                                                                                          values,
                                                                                                          ByteOrder,
                                                                                                          _operationTimeout,
                                                                                                          _cancellationToken),
                                                 new short[] { 1, 2 });
        }

        [TestMethod]
        public async Task WriteMultipleHoldingRegistersAsUShort()
        {
            await WriteHoldingRegistersCoreAsync(values => _sut.WriteMultipleHoldingRegistersAsUShortAsync(UnitIdentifier,
                                                                                                           StartingAddress,
                                                                                                           values,
                                                                                                           ByteOrder,
                                                                                                           _operationTimeout,
                                                                                                           _cancellationToken),
                                                 new ushort[] { 1, 2 });
        }

        [TestMethod]
        public async Task WriteMultipleHoldingRegistersAsInt()
        {
            await WriteHoldingRegistersCoreAsync(values => _sut.WriteMultipleHoldingRegistersAsIntAsync(UnitIdentifier,
                                                                                                        StartingAddress,
                                                                                                        values,
                                                                                                        ByteOrder,
                                                                                                        WordOrder32,
                                                                                                        _operationTimeout,
                                                                                                        _cancellationToken),
                                                 [1, 2],
                                                 VerifySwapWords32Invoked);
        }

        [TestMethod]
        public async Task WriteMultipleHoldingRegistersAsUInt()
        {
            await WriteHoldingRegistersCoreAsync(values => _sut.WriteMultipleHoldingRegistersAsUIntAsync(UnitIdentifier,
                                                                                                         StartingAddress,
                                                                                                         values,
                                                                                                         ByteOrder,
                                                                                                         WordOrder32,
                                                                                                         _operationTimeout,
                                                                                                         _cancellationToken),
                                                 [1U, 2U],
                                                 VerifySwapWords32Invoked);
        }

        [TestMethod]
        public async Task WriteMultipleHoldingRegistersAsFloat()
        {
            await WriteHoldingRegistersCoreAsync(values => _sut.WriteMultipleHoldingRegistersAsFloatAsync(UnitIdentifier,
                                                                                                          StartingAddress,
                                                                                                          values,
                                                                                                          ByteOrder,
                                                                                                          WordOrder32,
                                                                                                          _operationTimeout,
                                                                                                          _cancellationToken),
                                                 [1.1f, 2.2f],
                                                 VerifySwapWords32Invoked);
        }

        [TestMethod]
        public async Task WriteMultipleHoldingRegistersAsLong()
        {
            await WriteHoldingRegistersCoreAsync(values => _sut.WriteMultipleHoldingRegistersAsLongAsync(UnitIdentifier,
                                                                                                         StartingAddress,
                                                                                                         values,
                                                                                                         ByteOrder,
                                                                                                         WordOrder64,
                                                                                                         _operationTimeout,
                                                                                                         _cancellationToken),
                                                 [1L, 2L],
                                                 VerifySwapWords64Invoked);
        }

        [TestMethod]
        public async Task WriteMultipleHoldingRegistersAsULong()
        {
            await WriteHoldingRegistersCoreAsync(values => _sut.WriteMultipleHoldingRegistersAsULongAsync(UnitIdentifier,
                                                                                                          StartingAddress,
                                                                                                          values,
                                                                                                          ByteOrder,
                                                                                                          WordOrder64,
                                                                                                          _operationTimeout,
                                                                                                          _cancellationToken),
                                                 [1UL, 2UL],
                                                 VerifySwapWords64Invoked);
        }

        [TestMethod]
        public async Task WriteMultipleHoldingRegistersAsDouble()
        {
            await WriteHoldingRegistersCoreAsync(values => _sut.WriteMultipleHoldingRegistersAsDoubleAsync(UnitIdentifier,
                                                                                                           StartingAddress,
                                                                                                           values,
                                                                                                           ByteOrder,
                                                                                                           WordOrder64,
                                                                                                           _operationTimeout,
                                                                                                           _cancellationToken),
                                                 [1.1, 2.2],
                                                 VerifySwapWords64Invoked);
        }

        [TestMethod]
        [DataRow(TextEncoding.Ascii)]
        [DataRow(TextEncoding.Utf8)]
        [DataRow(TextEncoding.Utf16Be)]
        [DataRow(TextEncoding.Utf16Le)]
        public async Task WriteMultipleHoldingRegistersAsString(TextEncoding textEncoding)
        {
            // Arrange
            const string value = "test";
            _dataConverterMock.Setup(converter => converter.ConvertStringToBytes(It.IsAny<string>(), It.IsAny<TextEncoding>())).Returns(_registerBytes);

            // Act
            await _sut.WriteMultipleHoldingRegistersAsStringAsync(UnitIdentifier,
                                                                  StartingAddress,
                                                                  value,
                                                                  textEncoding,
                                                                  _operationTimeout,
                                                                  _cancellationToken);

            // Assert
            _dataConverterMock.Verify(converter => converter.ConvertStringToBytes(value, textEncoding), Times.Once);
            VerifyWriteMultipleRegistersInvoked();
        }

        [TestMethod]
        public void DisposeClientProxy()
        {
            // Arrange

            // Act
            _sut.Dispose();

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.Dispose(), Times.Once);
        }

        [TestMethod]
        public void NotThrowWhenClientProxyThrowsOnDisposal()
        {
            // Arrange
            _clientProxyMock.Setup(clientProxy => clientProxy.Dispose()).Throws<Exception>();

            // Act / Assert
            _sut.Dispose();
        }

        [TestMethod]
        public void NotDisposeClientProxyWhenAlreadyDisposed()
        {
            // Arrange
            _sut.Dispose();
            _clientProxyMock.Invocations.Clear();

            // Act
            _sut.Dispose();

            // Assert
            _clientProxyMock.Verify(clientProxy => clientProxy.Dispose(), Times.Never);
        }

        private async Task InvokeMethodAsync(TargetMethod targetMethod, TimeSpan? operationTimeout = null, CancellationToken? cancellationToken = null)
        {
            switch (targetMethod)
            {
                case TargetMethod.ReadDiscreteInputsAsync:
                    await _sut.ReadDiscreteInputsAsync(UnitIdentifier, StartingAddress, Quantity, operationTimeout ?? _operationTimeout, cancellationToken ?? _cancellationToken);
                    break;
                case TargetMethod.ReadCoilsAsync:
                    await _sut.ReadCoilsAsync(UnitIdentifier, StartingAddress, Quantity, operationTimeout ?? _operationTimeout, cancellationToken ?? _cancellationToken);
                    break;
                case TargetMethod.WriteSingleCoilAsync:
                    await _sut.WriteSingleCoilAsync(UnitIdentifier, StartingAddress, true, operationTimeout ?? _operationTimeout, cancellationToken ?? _cancellationToken);
                    break;
                case TargetMethod.WriteMultipleCoilsAsync:
                    await _sut.WriteMultipleCoilsAsync(UnitIdentifier,
                                                       StartingAddress,
                                                       [true, false],
                                                       operationTimeout ?? _operationTimeout,
                                                       cancellationToken ?? _cancellationToken);
                    break;
                case TargetMethod.ReadInputRegistersAsFloatAsync:
                    await _sut.ReadInputRegistersAsFloatAsync(UnitIdentifier,
                                                              StartingAddress,
                                                              Count,
                                                              ByteOrder,
                                                              WordOrder32,
                                                              operationTimeout ?? _operationTimeout,
                                                              cancellationToken ?? _cancellationToken);
                    break;
                case TargetMethod.ReadHoldingRegistersAsIntAsync:
                    await _sut.ReadHoldingRegistersAsIntAsync(UnitIdentifier,
                                                              StartingAddress,
                                                              Count,
                                                              ByteOrder,
                                                              WordOrder32,
                                                              operationTimeout ?? _operationTimeout,
                                                              cancellationToken ?? _cancellationToken);
                    break;
                case TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync:
                    await _sut.WriteMultipleHoldingRegistersAsDoubleAsync(UnitIdentifier,
                                                                          StartingAddress,
                                                                          [1.1, 2.2, 3.3],
                                                                          ByteOrder,
                                                                          WordOrder64,
                                                                          operationTimeout ?? _operationTimeout,
                                                                          cancellationToken ?? _cancellationToken);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(targetMethod), targetMethod, null);
            }
        }

        private async Task ReadRegistersCoreAsync<T>(Func<Task<T[]>> operation, T[] expectedValues, int bytesPerValue, Action? additionalVerifications = null)
            where T : unmanaged
        {
            // Arrange
            SetupReadInputRegistersReturns();
            SetupReadHoldingRegistersReturns();
            _dataConverterMock.Setup(dataConverter => dataConverter.CastFromBytes<T>(It.IsAny<Memory<byte>>())).Returns(expectedValues);
            _dataConverterMock.Setup(dataConverter => dataConverter.ConvertCountToQuantity(It.IsAny<uint>(), It.IsAny<int>())).Returns(Quantity);

            // Act
            var actualValues = await operation();

            // Assert
            _validatorMock.Verify(validator => validator.ValidateResponseAlignment(_registerBytes.Length, bytesPerValue, UnitIdentifier, StartingAddress), Times.Once);
            VerifySwapBytesInvoked();
            additionalVerifications?.Invoke();
            CollectionAssert.AreEqual(expectedValues, actualValues);
        }

        private async Task WriteHoldingRegistersCoreAsync<T>(Func<T[], Task> operation, T[] values, Action? additionalVerifications = null)
            where T : unmanaged
        {
            // Arrange
            _dataConverterMock.Setup(dataConverter => dataConverter.CastToBytes(It.IsAny<T[]>())).Returns(_registerBytes);

            // Act
            await operation(values);

            // Assert
            _dataConverterMock.Verify(dataConverter => dataConverter.CastToBytes(values), Times.Once);
            VerifySwapBytesInvoked();
            VerifyWriteMultipleRegistersInvoked();
            additionalVerifications?.Invoke();
        }

        private void SetupReadInputRegistersReturns()
        {
            _clientProxyMock.Setup(clientProxy => clientProxy.ReadInputRegistersAsync(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                            .ReturnsAsync(_registerBytes);
        }

        private void SetupReadHoldingRegistersReturns()
        {
            _clientProxyMock.Setup(clientProxy => clientProxy.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                            .ReturnsAsync(_registerBytes);
        }

        private void SetupConvertBitsToBoolsReturns(bool[] returnedBools)
        {
            _dataConverterMock.Setup(dataConverter => dataConverter.ConvertBitsToBools(It.IsAny<Memory<byte>>(), It.IsAny<ushort>())).Returns(returnedBools);
        }

        private void SetupGetBytesReturns()
        {
            _dataConverterMock.Setup(dataConverter => dataConverter.GetBytes(It.IsAny<short>())).Returns(_registerBytes);
            _dataConverterMock.Setup(dataConverter => dataConverter.GetBytes(It.IsAny<ushort>())).Returns(_registerBytes);
        }

        private void SetupReturns(TargetMethod targetMethod)
        {
            switch (targetMethod)
            {
                case TargetMethod.ReadDiscreteInputsAsync:
                    _clientProxyMock.Setup(proxy => proxy.ReadDiscreteInputsAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                                    .Returns((int _, ushort _, ushort _, CancellationToken ct) =>
                                             {
                                                 ct.ThrowIfCancellationRequested();
                                                 return Task.FromResult(Memory<byte>.Empty);
                                             });
                    break;
                case TargetMethod.ReadCoilsAsync:
                    _clientProxyMock.Setup(proxy => proxy.ReadCoilsAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                                    .Returns((int _, ushort _, ushort _, CancellationToken ct) =>
                                             {
                                                 ct.ThrowIfCancellationRequested();
                                                 return Task.FromResult(Memory<byte>.Empty);
                                             });
                    break;
                case TargetMethod.WriteSingleCoilAsync:
                    _clientProxyMock.Setup(proxy => proxy.WriteSingleCoilAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                                    .Returns((int _, ushort _, bool _, CancellationToken ct) =>
                                             {
                                                 ct.ThrowIfCancellationRequested();
                                                 return Task.CompletedTask;
                                             });
                    break;
                case TargetMethod.WriteMultipleCoilsAsync:
                    _clientProxyMock.Setup(proxy => proxy.WriteMultipleCoilsAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<bool[]>(), It.IsAny<CancellationToken>()))
                                    .Returns((int _, ushort _, bool[] _, CancellationToken ct) =>
                                             {
                                                 ct.ThrowIfCancellationRequested();
                                                 return Task.CompletedTask;
                                             });
                    break;
                case TargetMethod.ReadInputRegistersAsFloatAsync:
                    _clientProxyMock.Setup(proxy => proxy.ReadInputRegistersAsync(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                                    .Returns((byte _, ushort _, ushort _, CancellationToken ct) =>
                                             {
                                                 ct.ThrowIfCancellationRequested();
                                                 return Task.FromResult(Memory<byte>.Empty);
                                             });

                    break;
                case TargetMethod.ReadHoldingRegistersAsIntAsync:
                    _clientProxyMock.Setup(proxy => proxy.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                                    .Returns((byte _, ushort _, ushort _, CancellationToken ct) =>
                                             {
                                                 ct.ThrowIfCancellationRequested();
                                                 return Task.FromResult(Memory<byte>.Empty);
                                             });

                    break;
                case TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync:
                    _clientProxyMock.Setup(proxy => proxy.WriteMultipleRegistersAsync(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                                    .Returns((byte _, ushort _, byte[] _, CancellationToken ct) =>
                                             {
                                                 ct.ThrowIfCancellationRequested();
                                                 return Task.CompletedTask;
                                             });

                    break;
                default: throw new ArgumentOutOfRangeException(nameof(targetMethod), targetMethod, null);
            }
        }

        private void SetupThrows(TargetMethod targetMethod, Exception exceptionToThrow)
        {
            switch (targetMethod)
            {
                case TargetMethod.ReadDiscreteInputsAsync:
                    _clientProxyMock.Setup(proxy => proxy.ReadDiscreteInputsAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                                    .Throws(exceptionToThrow);
                    break;
                case TargetMethod.ReadCoilsAsync:
                    _clientProxyMock.Setup(proxy => proxy.ReadCoilsAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                                    .Throws(exceptionToThrow);
                    break;
                case TargetMethod.WriteSingleCoilAsync:
                    _clientProxyMock.Setup(proxy => proxy.WriteSingleCoilAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                                    .Throws(exceptionToThrow);
                    break;
                case TargetMethod.WriteMultipleCoilsAsync:
                    _clientProxyMock.Setup(proxy => proxy.WriteMultipleCoilsAsync(It.IsAny<int>(), It.IsAny<ushort>(), It.IsAny<bool[]>(), It.IsAny<CancellationToken>()))
                                    .Throws(exceptionToThrow);
                    break;
                case TargetMethod.ReadInputRegistersAsFloatAsync:
                    _clientProxyMock.Setup(proxy => proxy.ReadInputRegistersAsync(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                                    .Throws(exceptionToThrow);
                    break;
                case TargetMethod.ReadHoldingRegistersAsIntAsync:
                    _clientProxyMock.Setup(proxy => proxy.ReadHoldingRegistersAsync(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<ushort>(), It.IsAny<CancellationToken>()))
                                    .Throws(exceptionToThrow);
                    break;
                case TargetMethod.WriteMultipleHoldingRegistersAsDoubleAsync:
                    _clientProxyMock.Setup(proxy => proxy.WriteMultipleRegistersAsync(It.IsAny<byte>(), It.IsAny<ushort>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                                    .Throws(exceptionToThrow);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(targetMethod), targetMethod, null);
            }
        }

        private void VerifyConvertBitsToBoolsInvoked()
        {
            _dataConverterMock.Verify(dataConverter => dataConverter.ConvertBitsToBools(_registerBytes, Quantity), Times.Once);
        }

        private void VerifyConvertCountToQuantityInvoked(int bytesPerValue)
        {
            _dataConverterMock.Verify(dataConverter => dataConverter.ConvertCountToQuantity(Count, bytesPerValue), Times.Once);
        }

        private void VerifyReadInputRegistersInvoked()
        {
            _clientProxyMock.Verify(clientProxy => clientProxy.ReadInputRegistersAsync(UnitIdentifier, StartingAddress, Quantity, It.IsAny<CancellationToken>()), Times.Once);
        }

        private void VerifyReadHoldingRegistersInvoked()
        {
            _clientProxyMock.Verify(clientProxy => clientProxy.ReadHoldingRegistersAsync(UnitIdentifier, StartingAddress, Quantity, It.IsAny<CancellationToken>()), Times.Once);
        }

        private void VerifySwapBytesInvoked()
        {
            _dataConverterMock.Verify(dataConverter => dataConverter.SwapBytes(_registerBytes, ByteOrder), Times.Once);
        }

        private void VerifySwapWords32Invoked()
        {
            _dataConverterMock.Verify(dataConverter => dataConverter.SwapWords(_registerBytes, WordOrder32), Times.Once);
        }

        private void VerifySwapWords64Invoked()
        {
            _dataConverterMock.Verify(dataConverter => dataConverter.SwapWords(_registerBytes, WordOrder64), Times.Once);
        }

        private void VerifyWriteSingleRegisterInvoked()
        {
            _clientProxyMock.Verify(clientProxy => clientProxy.WriteSingleRegisterAsync(UnitIdentifier, StartingAddress, _registerBytes, It.IsAny<CancellationToken>()),
                                    Times.Once);
        }

        private void VerifyWriteMultipleRegistersInvoked()
        {
            _clientProxyMock.Verify(clientProxy => clientProxy.WriteMultipleRegistersAsync(UnitIdentifier, StartingAddress, _registerBytes, It.IsAny<CancellationToken>()),
                                    Times.Once);
        }
    }

    public class TestException : Exception
    {
    }
}