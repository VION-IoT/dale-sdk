using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Client.LogicBlock
{
    [TestClass]
    public class LogicBlockModbusTcpClientShould
    {
        private const ushort Quantity = 17;

        private const ushort StartingAddress = 42;

        private const TextEncoding TextEncoding = Core.Conversion.TextEncoding.Utf8;

        private const int UnitIdentifier = 1;

        private const ushort RegisterAddress = 5;

        private const uint Count = 3;

        private const ByteOrder ByteOrder = Core.Conversion.ByteOrder.LsbToMsb;

        private const WordOrder32 WordOrder32 = Core.Conversion.WordOrder32.LswToMsw;

        private const WordOrder64 WordOrder64 = Core.Conversion.WordOrder64.BADC;

        private readonly Mock<IModbusTcpClientWrapper> _clientWrapperMock = new();

        private readonly Action<Exception> _controlErrorCallback = _ => { };

        private readonly Action _controlSuccessCallback = () => { };

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Action<Exception, ModbusReceipt> _errorCallback = (_, _) => { };

        private readonly Mock<ILogger<LogicBlockModbusTcpClient>> _loggerMock = new();

        private readonly TimeSpan _operationTimeout = TimeSpan.FromSeconds(42);

        private readonly Mock<IRequestQueue> _requestQueueMock = new();

        private readonly Action<ModbusReceipt> _voidResultSuccessCallback = _ => { };

        private Func<CancellationToken, Task>? _capturedOperation;

        private LogicBlockModbusTcpClient _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = new LogicBlockModbusTcpClient(_clientWrapperMock.Object, _requestQueueMock.Object, _loggerMock.Object);

            _requestQueueMock.Setup(queue => queue.Enqueue(It.IsAny<string>(),
                                                           It.IsAny<IActorDispatcher>(),
                                                           It.IsAny<Func<CancellationToken, Task>>(),
                                                           It.IsAny<Action<ModbusReceipt>?>(),
                                                           It.IsAny<Action<Exception, ModbusReceipt>?>()))
                             .Callback<string, IActorDispatcher, Func<CancellationToken, Task>, Action<ModbusReceipt>?, Action<Exception, ModbusReceipt>
                                 ?>((_, _, operation, _, _) =>
                                        _capturedOperation = operation);

            _requestQueueMock.Setup(queue => queue.EnqueueControlOperation(It.IsAny<string>(),
                                                                           It.IsAny<IActorDispatcher>(),
                                                                           It.IsAny<Func<CancellationToken, Task>>(),
                                                                           It.IsAny<Action?>(),
                                                                           It.IsAny<Action<Exception>?>()))
                             .Callback<string, IActorDispatcher, Func<CancellationToken, Task>, Action?, Action<Exception>?>((_, _, operation, _, _) =>
                                 _capturedOperation = operation);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.4")]
        public void InitializeRequestQueueWhenClientEnabled()
        {
            // Arrange

            // Act
            _sut.IsEnabled = true;

            // Assert
            _requestQueueMock.Verify(queue => queue.Initialize(It.IsAny<int>(), It.IsAny<QueueOverflowPolicy>(), It.IsAny<ModbusLinkAccumulator>()), Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.4")]
        public void NotReinitializeRequestQueueAfterInitialization()
        {
            // Arrange
            _sut.IsEnabled = true;
            _requestQueueMock.Invocations.Clear();

            // Act
            _sut.IsEnabled = true;
            _sut.IsEnabled = true;

            // Assert
            _requestQueueMock.Verify(queue => queue.Initialize(It.IsAny<int>(), It.IsAny<QueueOverflowPolicy>(), It.IsAny<ModbusLinkAccumulator>()), Times.Never);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-009.3")]
        [DataRow(50, QueueOverflowPolicy.DropOldest)]
        [DataRow(100, QueueOverflowPolicy.DropNewest)]
        [DataRow(200, QueueOverflowPolicy.RejectNew)]
        public void InitializeRequestQueueWithConfiguredCapacityAndOverflowPolicy(int capacity, QueueOverflowPolicy overflowPolicy)
        {
            // Arrange
            _sut.QueueCapacity = capacity;
            _sut.QueueOverflowPolicy = overflowPolicy;

            // Act
            _sut.IsEnabled = true;

            // Assert
            _requestQueueMock.Verify(queue => queue.Initialize(capacity, overflowPolicy, It.IsAny<ModbusLinkAccumulator>()), Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-009.2")]
        public void RefuseQueueCapacityChangeAfterInitialization()
        {
            // Arrange
            const int initialCapacity = 5;
            _sut.QueueCapacity = initialCapacity;
            _sut.IsEnabled = true;

            // Act & Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.QueueCapacity = 2);
            Assert.AreEqual(initialCapacity, _sut.QueueCapacity);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-009.2")]
        public void RefuseQueueOverflowPolicyChangeAfterInitialization()
        {
            // Arrange
            const QueueOverflowPolicy initialOverflowPolicy = QueueOverflowPolicy.DropNewest;
            _sut.QueueOverflowPolicy = initialOverflowPolicy;
            _sut.IsEnabled = true;

            // Act & Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.QueueOverflowPolicy = QueueOverflowPolicy.RejectNew);
            Assert.AreEqual(initialOverflowPolicy, _sut.QueueOverflowPolicy);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-009.2")]
        public void AcceptQueueCapacityAlreadyInForceAfterInitialization()
        {
            // Arrange
            const int initialCapacity = 5;
            _sut.QueueCapacity = initialCapacity;
            _sut.QueueOverflowPolicy = QueueOverflowPolicy.DropNewest;
            _sut.IsEnabled = true;

            // Act
            _sut.QueueCapacity = initialCapacity;
            _sut.QueueOverflowPolicy = QueueOverflowPolicy.DropNewest;

            // Assert
            Assert.AreEqual(initialCapacity, _sut.QueueCapacity);
            Assert.AreEqual(QueueOverflowPolicy.DropNewest, _sut.QueueOverflowPolicy);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.3")]
        public async Task RunRequestAcceptedBeforeClientWasDisabled()
        {
            // Arrange
            _sut.IsEnabled = true;
            SetupArrayResultOperationCapture<bool>();
            _sut.ReadCoils(UnitIdentifier, StartingAddress, Quantity, _dispatcherMock.Object, ArrayResultSuccessCallback<bool>(), _errorCallback, _operationTimeout);

            // Act
            _sut.IsEnabled = false;
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadCoilsAsync(UnitIdentifier, StartingAddress, Quantity, _operationTimeout, CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-003.3")]
        public async Task UseDefaultOperationTimeoutInForceWhenRequestWasAccepted()
        {
            // Arrange
            var timeoutAtAcceptance = TimeSpan.FromSeconds(7);
            _sut.IsEnabled = true;
            _sut.DefaultOperationTimeout = timeoutAtAcceptance;
            SetupArrayResultOperationCapture<bool>();
            _sut.ReadCoils(UnitIdentifier, StartingAddress, Quantity, _dispatcherMock.Object, ArrayResultSuccessCallback<bool>(), _errorCallback);

            // Act
            _sut.DefaultOperationTimeout = TimeSpan.FromSeconds(9);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadCoilsAsync(UnitIdentifier, StartingAddress, Quantity, timeoutAtAcceptance, CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-009.5")]
        public void ReturnQueuedRequestCountFromQueue()
        {
            // Arrange
            const int expectedQueuedCount = 7;
            _requestQueueMock.SetupGet(queue => queue.QueuedRequestCount).Returns(expectedQueuedCount);

            // Act
            var actualQueuedCount = _sut.QueuedRequestCount;

            // Assert
            Assert.AreEqual(expectedQueuedCount, actualQueuedCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-006.3")]
        [DataRow(-1, DisplayName = "Less than minimum allowed port number")]
        [DataRow(0, DisplayName = "The unconfigured-port sentinel")]
        [DataRow(65536, DisplayName = "Larger than maximum allowed port number")]
        public void ThrowExceptionWhenPortInvalid(int port)
        {
            // Arrange

            // Act & Assert
            Assert.Throws<FormatException>(() => _sut.Port = port);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-003.4")]
        [DataRow(0, DisplayName = "Zero")]
        [DataRow(-1, DisplayName = "Negative")]
        public void ThrowExceptionWhenConnectionTimeoutNotPositive(int seconds)
        {
            // Arrange

            // Act & Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _sut.ConnectionTimeout = TimeSpan.FromSeconds(seconds));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-003.4")]
        [DataRow(0, DisplayName = "Zero")]
        [DataRow(-1, DisplayName = "Negative")]
        public void ThrowExceptionWhenDefaultOperationTimeoutNotPositive(int seconds)
        {
            // Arrange

            // Act & Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _sut.DefaultOperationTimeout = TimeSpan.FromSeconds(seconds));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-006.2")]
        public void ApplyDefaultPortOnConstruction()
        {
            // Arrange

            // Act / Assert
            _clientWrapperMock.VerifySet(client => client.Port = 502, Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-006.1")]
        public void ForwardConfiguredPortToWrapper()
        {
            // Arrange
            const int port = 2000;

            // Act
            _sut.Port = port;

            // Assert
            _clientWrapperMock.VerifySet(client => client.Port = port, Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-006.1")]
        public void ReturnConfiguredPortFromWrapper()
        {
            // Arrange
            const int expectedPort = 2000;
            _clientWrapperMock.SetupGet(client => client.Port).Returns(expectedPort);

            // Act
            var actualPort = _sut.Port;

            // Assert
            Assert.AreEqual(expectedPort, actualPort);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-006.4")]
        [DataRow(null, DisplayName = "Null IP address")]
        [DataRow("", DisplayName = "Empty IP address")]
        [DataRow("   ", DisplayName = "Whitespace IP address")]
        [DataRow("0.0.0.257", DisplayName = "Malformed IP address")]
        public void ThrowExceptionWhenIpAddressInvalid(string ip)
        {
            // Arrange

            // Act & Assert
            Assert.Throws<FormatException>(() => _sut.IpAddress = ip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-006.1")]
        public void ForwardConfiguredIpAddressToWrapper()
        {
            // Arrange
            var expectedIp = IPAddress.Parse("127.0.0.1");

            // Act
            _sut.IpAddress = expectedIp.ToString();

            // Assert
            _clientWrapperMock.VerifySet(client => client.IpAddress = expectedIp, Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-006.1")]
        public void ReturnConfiguredIpAddressFromWrapper()
        {
            // Arrange
            var expectedIp = IPAddress.Parse("127.0.0.1");
            _clientWrapperMock.SetupGet(client => client.IpAddress).Returns(expectedIp);

            // Act
            var actualIp = _sut.IpAddress;

            // Assert
            Assert.AreEqual(expectedIp.ToString(), actualIp);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-003.2")]
        public void ForwardConfiguredConnectionTimeoutToWrapper()
        {
            // Arrange
            var expectedTimeout = TimeSpan.FromSeconds(42);

            // Act
            _sut.ConnectionTimeout = expectedTimeout;

            // Assert
            _clientWrapperMock.VerifySet(client => client.ConnectionTimeout = expectedTimeout, Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-003.2")]
        public void ReturnConfiguredConnectionTimeoutFromWrapper()
        {
            // Arrange
            var expectedTimeout = TimeSpan.FromSeconds(42);
            _clientWrapperMock.SetupGet(client => client.ConnectionTimeout).Returns(expectedTimeout);

            // Act
            var actualTimeout = _sut.ConnectionTimeout;

            // Assert
            Assert.AreEqual(expectedTimeout, actualTimeout);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueDisconnectWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.Disconnect(_dispatcherMock.Object);

            // Assert
            VerifyControlOperationNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        public async Task EnqueueDisconnectWhenClientEnabled()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act
            _sut.Disconnect(_dispatcherMock.Object, _controlSuccessCallback, _controlErrorCallback);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyControlOperationEnqueued(nameof(_sut.Disconnect));
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadCoilsWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadCoils(UnitIdentifier,
                           RegisterAddress,
                           Quantity,
                           _dispatcherMock.Object,
                           ArrayResultSuccessCallback<bool>(),
                           _errorCallback,
                           _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<bool>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadCoilsWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<bool>();
            SetupArrayResultOperationCapture<bool>();

            // Act
            _sut.ReadCoils(UnitIdentifier,
                           StartingAddress,
                           Quantity,
                           _dispatcherMock.Object,
                           successCallback,
                           _errorCallback,
                           operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadCoils), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadCoilsAsync(UnitIdentifier, StartingAddress, Quantity, operationTimeout, CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteSingleCoilWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteSingleCoil(UnitIdentifier,
                                 RegisterAddress,
                                 true,
                                 _dispatcherMock.Object,
                                 _voidResultSuccessCallback,
                                 _errorCallback,
                                 _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteSingleCoilWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            const bool value = true;

            // Act
            _sut.WriteSingleCoil(UnitIdentifier,
                                 RegisterAddress,
                                 value,
                                 _dispatcherMock.Object,
                                 _voidResultSuccessCallback,
                                 _errorCallback,
                                 operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteSingleCoil));
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.WriteSingleCoilAsync(UnitIdentifier, RegisterAddress, value, operationTimeout, CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleCoilsWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleCoils(UnitIdentifier,
                                    RegisterAddress,
                                    [true, false, true],
                                    _dispatcherMock.Object,
                                    _voidResultSuccessCallback,
                                    _errorCallback,
                                    _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleCoilsWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            bool[] values = [true, false, true];

            // Act
            _sut.WriteMultipleCoils(UnitIdentifier,
                                    RegisterAddress,
                                    values,
                                    _dispatcherMock.Object,
                                    _voidResultSuccessCallback,
                                    _errorCallback,
                                    operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleCoils));
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.WriteMultipleCoilsAsync(UnitIdentifier, RegisterAddress, values, operationTimeout, CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadDiscreteInputsWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadDiscreteInputs(UnitIdentifier,
                                    StartingAddress,
                                    Quantity,
                                    _dispatcherMock.Object,
                                    ArrayResultSuccessCallback<bool>(),
                                    _errorCallback,
                                    _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<bool>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadDiscreteInputsWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<bool>();
            SetupArrayResultOperationCapture<bool>();

            // Act
            _sut.ReadDiscreteInputs(UnitIdentifier,
                                    StartingAddress,
                                    Quantity,
                                    _dispatcherMock.Object,
                                    successCallback,
                                    _errorCallback,
                                    operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadDiscreteInputs), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadDiscreteInputsAsync(UnitIdentifier, StartingAddress, Quantity, operationTimeout, CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersRawWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersRaw(UnitIdentifier,
                                       StartingAddress,
                                       Quantity,
                                       _dispatcherMock.Object,
                                       ArrayResultSuccessCallback<byte>(),
                                       _errorCallback,
                                       _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<byte>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersRawWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<byte>();
            SetupArrayResultOperationCapture<byte>();

            // Act
            _sut.ReadInputRegistersRaw(UnitIdentifier,
                                       StartingAddress,
                                       Quantity,
                                       _dispatcherMock.Object,
                                       successCallback,
                                       _errorCallback,
                                       operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadInputRegistersRaw), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersRawAsync(UnitIdentifier,
                                                                                                StartingAddress,
                                                                                                Quantity,
                                                                                                operationTimeout,
                                                                                                CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersAsShortWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersAsShort(UnitIdentifier,
                                           StartingAddress,
                                           Quantity,
                                           _dispatcherMock.Object,
                                           ArrayResultSuccessCallback<short>(),
                                           _errorCallback,
                                           ByteOrder,
                                           _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<short>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersAsShortWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<short>();
            SetupArrayResultOperationCapture<short>();

            // Act
            _sut.ReadInputRegistersAsShort(UnitIdentifier,
                                           StartingAddress,
                                           Quantity,
                                           _dispatcherMock.Object,
                                           successCallback,
                                           _errorCallback,
                                           ByteOrder,
                                           operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadInputRegistersAsShort), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersAsShortAsync(UnitIdentifier,
                                                                                                    StartingAddress,
                                                                                                    Quantity,
                                                                                                    ByteOrder,
                                                                                                    operationTimeout,
                                                                                                    CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersAsUShortWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersAsUShort(UnitIdentifier,
                                            StartingAddress,
                                            Quantity,
                                            _dispatcherMock.Object,
                                            ArrayResultSuccessCallback<ushort>(),
                                            _errorCallback,
                                            ByteOrder,
                                            _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<ushort>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersAsUShortWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;

            var successCallback = ArrayResultSuccessCallback<ushort>();
            SetupArrayResultOperationCapture<ushort>();

            // Act
            _sut.ReadInputRegistersAsUShort(UnitIdentifier,
                                            StartingAddress,
                                            Quantity,
                                            _dispatcherMock.Object,
                                            successCallback,
                                            _errorCallback,
                                            ByteOrder,
                                            operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadInputRegistersAsUShort), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersAsUShortAsync(UnitIdentifier,
                                                                                                     StartingAddress,
                                                                                                     Quantity,
                                                                                                     ByteOrder,
                                                                                                     operationTimeout,
                                                                                                     CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersAsIntWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersAsInt(UnitIdentifier,
                                         StartingAddress,
                                         Count,
                                         _dispatcherMock.Object,
                                         ArrayResultSuccessCallback<int>(),
                                         _errorCallback,
                                         ByteOrder,
                                         WordOrder32,
                                         _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<int>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersAsIntWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<int>();
            SetupArrayResultOperationCapture<int>();

            // Act
            _sut.ReadInputRegistersAsInt(UnitIdentifier,
                                         StartingAddress,
                                         Count,
                                         _dispatcherMock.Object,
                                         successCallback,
                                         _errorCallback,
                                         ByteOrder,
                                         WordOrder32,
                                         operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadInputRegistersAsInt), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersAsIntAsync(UnitIdentifier,
                                                                                                  StartingAddress,
                                                                                                  Count,
                                                                                                  ByteOrder,
                                                                                                  WordOrder32,
                                                                                                  operationTimeout,
                                                                                                  CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersAsUIntWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersAsUInt(UnitIdentifier,
                                          StartingAddress,
                                          Count,
                                          _dispatcherMock.Object,
                                          ArrayResultSuccessCallback<uint>(),
                                          _errorCallback,
                                          ByteOrder,
                                          WordOrder32,
                                          _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<uint>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersAsUIntWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;

            var successCallback = ArrayResultSuccessCallback<uint>();
            SetupArrayResultOperationCapture<uint>();

            // Act
            _sut.ReadInputRegistersAsUInt(UnitIdentifier,
                                          StartingAddress,
                                          Count,
                                          _dispatcherMock.Object,
                                          successCallback,
                                          _errorCallback,
                                          ByteOrder,
                                          WordOrder32,
                                          operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadInputRegistersAsUInt), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersAsUIntAsync(UnitIdentifier,
                                                                                                   StartingAddress,
                                                                                                   Count,
                                                                                                   ByteOrder,
                                                                                                   WordOrder32,
                                                                                                   operationTimeout,
                                                                                                   CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersAsFloatWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersAsFloat(UnitIdentifier,
                                           StartingAddress,
                                           Count,
                                           _dispatcherMock.Object,
                                           ArrayResultSuccessCallback<float>(),
                                           _errorCallback,
                                           ByteOrder,
                                           WordOrder32,
                                           _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<float>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersAsFloatWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<float>();
            SetupArrayResultOperationCapture<float>();

            // Act
            _sut.ReadInputRegistersAsFloat(UnitIdentifier,
                                           StartingAddress,
                                           Count,
                                           _dispatcherMock.Object,
                                           successCallback,
                                           _errorCallback,
                                           ByteOrder,
                                           WordOrder32,
                                           operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadInputRegistersAsFloat), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersAsFloatAsync(UnitIdentifier,
                                                                                                    StartingAddress,
                                                                                                    Count,
                                                                                                    ByteOrder,
                                                                                                    WordOrder32,
                                                                                                    operationTimeout,
                                                                                                    CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersAsLongWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersAsLong(UnitIdentifier,
                                          StartingAddress,
                                          Count,
                                          _dispatcherMock.Object,
                                          ArrayResultSuccessCallback<long>(),
                                          _errorCallback,
                                          ByteOrder,
                                          WordOrder64,
                                          _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<long>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersAsLongWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<long>();
            SetupArrayResultOperationCapture<long>();

            // Act
            _sut.ReadInputRegistersAsLong(UnitIdentifier,
                                          StartingAddress,
                                          Count,
                                          _dispatcherMock.Object,
                                          successCallback,
                                          _errorCallback,
                                          ByteOrder,
                                          WordOrder64,
                                          operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadInputRegistersAsLong), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersAsLongAsync(UnitIdentifier,
                                                                                                   StartingAddress,
                                                                                                   Count,
                                                                                                   ByteOrder,
                                                                                                   WordOrder64,
                                                                                                   operationTimeout,
                                                                                                   CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersAsULongWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersAsULong(UnitIdentifier,
                                           StartingAddress,
                                           Count,
                                           _dispatcherMock.Object,
                                           ArrayResultSuccessCallback<ulong>(),
                                           _errorCallback,
                                           ByteOrder,
                                           WordOrder64,
                                           _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<ulong>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersAsULongWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<ulong>();
            SetupArrayResultOperationCapture<ulong>();

            // Act
            _sut.ReadInputRegistersAsULong(UnitIdentifier,
                                           StartingAddress,
                                           Count,
                                           _dispatcherMock.Object,
                                           successCallback,
                                           _errorCallback,
                                           ByteOrder,
                                           WordOrder64,
                                           operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadInputRegistersAsULong), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersAsULongAsync(UnitIdentifier,
                                                                                                    StartingAddress,
                                                                                                    Count,
                                                                                                    ByteOrder,
                                                                                                    WordOrder64,
                                                                                                    operationTimeout,
                                                                                                    CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersAsDoubleWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersAsDouble(UnitIdentifier,
                                            StartingAddress,
                                            Count,
                                            _dispatcherMock.Object,
                                            ArrayResultSuccessCallback<double>(),
                                            _errorCallback,
                                            ByteOrder,
                                            WordOrder64,
                                            _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<double>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersAsDoubleWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<double>();
            SetupArrayResultOperationCapture<double>();

            // Act
            _sut.ReadInputRegistersAsDouble(UnitIdentifier,
                                            StartingAddress,
                                            Count,
                                            _dispatcherMock.Object,
                                            successCallback,
                                            _errorCallback,
                                            ByteOrder,
                                            WordOrder64,
                                            operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadInputRegistersAsDouble), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersAsDoubleAsync(UnitIdentifier,
                                                                                                     StartingAddress,
                                                                                                     Count,
                                                                                                     ByteOrder,
                                                                                                     WordOrder64,
                                                                                                     operationTimeout,
                                                                                                     CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadInputRegistersAsStringWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadInputRegistersAsString(UnitIdentifier,
                                            StartingAddress,
                                            Quantity,
                                            _dispatcherMock.Object,
                                            SingleResultSuccessCallback<string>(),
                                            _errorCallback,
                                            TextEncoding,
                                            _operationTimeout);

            // Assert
            VerifySingleRequestResultNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadInputRegistersAsStringWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = SingleResultSuccessCallback<string>();
            SetupSingleResultOperationCapture<string>();

            // Act
            _sut.ReadInputRegistersAsString(UnitIdentifier,
                                            StartingAddress,
                                            Quantity,
                                            _dispatcherMock.Object,
                                            successCallback,
                                            _errorCallback,
                                            TextEncoding,
                                            operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifySingleRequestResultEnqueued(nameof(_sut.ReadInputRegistersAsString), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadInputRegistersAsStringAsync(UnitIdentifier,
                                                                                                     StartingAddress,
                                                                                                     Quantity,
                                                                                                     TextEncoding,
                                                                                                     operationTimeout,
                                                                                                     CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersRawWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersRaw(UnitIdentifier,
                                         StartingAddress,
                                         Quantity,
                                         _dispatcherMock.Object,
                                         ArrayResultSuccessCallback<byte>(),
                                         _errorCallback,
                                         _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<byte>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersRawWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<byte>();
            SetupArrayResultOperationCapture<byte>();

            // Act
            _sut.ReadHoldingRegistersRaw(UnitIdentifier,
                                         StartingAddress,
                                         Quantity,
                                         _dispatcherMock.Object,
                                         successCallback,
                                         _errorCallback,
                                         operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadHoldingRegistersRaw), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersRawAsync(UnitIdentifier,
                                                                                                  StartingAddress,
                                                                                                  Quantity,
                                                                                                  operationTimeout,
                                                                                                  CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersAsShortWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersAsShort(UnitIdentifier,
                                             StartingAddress,
                                             Quantity,
                                             _dispatcherMock.Object,
                                             ArrayResultSuccessCallback<short>(),
                                             _errorCallback,
                                             ByteOrder,
                                             _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<short>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersAsShortWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<short>();
            SetupArrayResultOperationCapture<short>();

            // Act
            _sut.ReadHoldingRegistersAsShort(UnitIdentifier,
                                             StartingAddress,
                                             Quantity,
                                             _dispatcherMock.Object,
                                             successCallback,
                                             _errorCallback,
                                             ByteOrder,
                                             operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadHoldingRegistersAsShort), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersAsShortAsync(UnitIdentifier,
                                                                                                      StartingAddress,
                                                                                                      Quantity,
                                                                                                      ByteOrder,
                                                                                                      operationTimeout,
                                                                                                      CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersAsUShortWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersAsUShort(UnitIdentifier,
                                              StartingAddress,
                                              Quantity,
                                              _dispatcherMock.Object,
                                              ArrayResultSuccessCallback<ushort>(),
                                              _errorCallback,
                                              ByteOrder,
                                              _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<ushort>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersAsUShortWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<ushort>();
            SetupArrayResultOperationCapture<ushort>();

            // Act
            _sut.ReadHoldingRegistersAsUShort(UnitIdentifier,
                                              StartingAddress,
                                              Quantity,
                                              _dispatcherMock.Object,
                                              successCallback,
                                              _errorCallback,
                                              ByteOrder,
                                              operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadHoldingRegistersAsUShort), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersAsUShortAsync(UnitIdentifier,
                                                                                                       StartingAddress,
                                                                                                       Quantity,
                                                                                                       ByteOrder,
                                                                                                       operationTimeout,
                                                                                                       CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersAsIntWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersAsInt(UnitIdentifier,
                                           StartingAddress,
                                           Count,
                                           _dispatcherMock.Object,
                                           ArrayResultSuccessCallback<int>(),
                                           _errorCallback,
                                           ByteOrder,
                                           WordOrder32,
                                           _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<int>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersAsIntWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<int>();
            SetupArrayResultOperationCapture<int>();

            // Act
            _sut.ReadHoldingRegistersAsInt(UnitIdentifier,
                                           StartingAddress,
                                           Count,
                                           _dispatcherMock.Object,
                                           successCallback,
                                           _errorCallback,
                                           ByteOrder,
                                           WordOrder32,
                                           operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadHoldingRegistersAsInt), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersAsIntAsync(UnitIdentifier,
                                                                                                    StartingAddress,
                                                                                                    Count,
                                                                                                    ByteOrder,
                                                                                                    WordOrder32,
                                                                                                    operationTimeout,
                                                                                                    CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersAsUIntWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersAsUInt(UnitIdentifier,
                                            StartingAddress,
                                            Count,
                                            _dispatcherMock.Object,
                                            ArrayResultSuccessCallback<uint>(),
                                            _errorCallback,
                                            ByteOrder,
                                            WordOrder32,
                                            _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<uint>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersAsUIntWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<uint>();
            SetupArrayResultOperationCapture<uint>();

            // Act
            _sut.ReadHoldingRegistersAsUInt(UnitIdentifier,
                                            StartingAddress,
                                            Count,
                                            _dispatcherMock.Object,
                                            successCallback,
                                            _errorCallback,
                                            ByteOrder,
                                            WordOrder32,
                                            operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadHoldingRegistersAsUInt), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersAsUIntAsync(UnitIdentifier,
                                                                                                     StartingAddress,
                                                                                                     Count,
                                                                                                     ByteOrder,
                                                                                                     WordOrder32,
                                                                                                     operationTimeout,
                                                                                                     CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersAsFloatWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersAsFloat(UnitIdentifier,
                                             StartingAddress,
                                             Count,
                                             _dispatcherMock.Object,
                                             ArrayResultSuccessCallback<float>(),
                                             _errorCallback,
                                             ByteOrder,
                                             WordOrder32,
                                             _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<float>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersAsFloatWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<float>();
            SetupArrayResultOperationCapture<float>();

            // Act
            _sut.ReadHoldingRegistersAsFloat(UnitIdentifier,
                                             StartingAddress,
                                             Count,
                                             _dispatcherMock.Object,
                                             successCallback,
                                             _errorCallback,
                                             ByteOrder,
                                             WordOrder32,
                                             operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadHoldingRegistersAsFloat), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersAsFloatAsync(UnitIdentifier,
                                                                                                      StartingAddress,
                                                                                                      Count,
                                                                                                      ByteOrder,
                                                                                                      WordOrder32,
                                                                                                      operationTimeout,
                                                                                                      CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersAsLongWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersAsLong(UnitIdentifier,
                                            StartingAddress,
                                            Count,
                                            _dispatcherMock.Object,
                                            ArrayResultSuccessCallback<long>(),
                                            _errorCallback,
                                            ByteOrder,
                                            WordOrder64,
                                            _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<long>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersAsLongWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<long>();
            SetupArrayResultOperationCapture<long>();

            // Act
            _sut.ReadHoldingRegistersAsLong(UnitIdentifier,
                                            StartingAddress,
                                            Count,
                                            _dispatcherMock.Object,
                                            successCallback,
                                            _errorCallback,
                                            ByteOrder,
                                            WordOrder64,
                                            operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadHoldingRegistersAsLong), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersAsLongAsync(UnitIdentifier,
                                                                                                     StartingAddress,
                                                                                                     Count,
                                                                                                     ByteOrder,
                                                                                                     WordOrder64,
                                                                                                     operationTimeout,
                                                                                                     CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersAsULongWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersAsULong(UnitIdentifier,
                                             StartingAddress,
                                             Count,
                                             _dispatcherMock.Object,
                                             ArrayResultSuccessCallback<ulong>(),
                                             _errorCallback,
                                             ByteOrder,
                                             WordOrder64,
                                             _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<ulong>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersAsULongWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<ulong>();
            SetupArrayResultOperationCapture<ulong>();

            // Act
            _sut.ReadHoldingRegistersAsULong(UnitIdentifier,
                                             StartingAddress,
                                             Count,
                                             _dispatcherMock.Object,
                                             successCallback,
                                             _errorCallback,
                                             ByteOrder,
                                             WordOrder64,
                                             operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadHoldingRegistersAsULong), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersAsULongAsync(UnitIdentifier,
                                                                                                      StartingAddress,
                                                                                                      Count,
                                                                                                      ByteOrder,
                                                                                                      WordOrder64,
                                                                                                      operationTimeout,
                                                                                                      CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersAsDoubleWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersAsDouble(UnitIdentifier,
                                              StartingAddress,
                                              Count,
                                              _dispatcherMock.Object,
                                              ArrayResultSuccessCallback<double>(),
                                              _errorCallback,
                                              ByteOrder,
                                              WordOrder64,
                                              _operationTimeout);

            // Assert
            VerifyArrayResultRequestNotEnqueued<double>();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersAsDoubleWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = ArrayResultSuccessCallback<double>();
            SetupArrayResultOperationCapture<double>();

            // Act
            _sut.ReadHoldingRegistersAsDouble(UnitIdentifier,
                                              StartingAddress,
                                              Count,
                                              _dispatcherMock.Object,
                                              successCallback,
                                              _errorCallback,
                                              ByteOrder,
                                              WordOrder64,
                                              operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyArrayResultRequestEnqueued(nameof(_sut.ReadHoldingRegistersAsDouble), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersAsDoubleAsync(UnitIdentifier,
                                                                                                       StartingAddress,
                                                                                                       Count,
                                                                                                       ByteOrder,
                                                                                                       WordOrder64,
                                                                                                       operationTimeout,
                                                                                                       CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueReadHoldingRegistersAsStringWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.ReadHoldingRegistersAsString(UnitIdentifier,
                                              StartingAddress,
                                              Quantity,
                                              _dispatcherMock.Object,
                                              SingleResultSuccessCallback<string>(),
                                              _errorCallback,
                                              TextEncoding,
                                              _operationTimeout);

            // Assert
            VerifySingleRequestResultNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueReadHoldingRegistersAsStringWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            var successCallback = SingleResultSuccessCallback<string>();
            SetupSingleResultOperationCapture<string>();

            // Act
            _sut.ReadHoldingRegistersAsString(UnitIdentifier,
                                              StartingAddress,
                                              Quantity,
                                              _dispatcherMock.Object,
                                              successCallback,
                                              _errorCallback,
                                              TextEncoding,
                                              operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifySingleRequestResultEnqueued(nameof(_sut.ReadHoldingRegistersAsString), successCallback);
            _clientWrapperMock.Verify(clientWrapper => clientWrapper.ReadHoldingRegistersAsStringAsync(UnitIdentifier,
                                                                                                       StartingAddress,
                                                                                                       Quantity,
                                                                                                       TextEncoding,
                                                                                                       operationTimeout,
                                                                                                       CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteSingleHoldingRegisterWithShortValueWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteSingleHoldingRegister(UnitIdentifier,
                                            RegisterAddress,
                                            -123,
                                            _dispatcherMock.Object,
                                            _voidResultSuccessCallback,
                                            _errorCallback,
                                            ByteOrder,
                                            _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteSingleHoldingRegisterWithShortValueWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            const short value = -321;

            // Act
            _sut.WriteSingleHoldingRegister(UnitIdentifier,
                                            RegisterAddress,
                                            value,
                                            _dispatcherMock.Object,
                                            _voidResultSuccessCallback,
                                            _errorCallback,
                                            ByteOrder,
                                            operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteSingleHoldingRegister));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteSingleHoldingRegisterAsync(UnitIdentifier,
                                                                                         RegisterAddress,
                                                                                         value,
                                                                                         ByteOrder,
                                                                                         operationTimeout,
                                                                                         CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteSingleHoldingRegisterWithUShortValueWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteSingleHoldingRegister(UnitIdentifier,
                                            RegisterAddress,
                                            (ushort)123,
                                            _dispatcherMock.Object,
                                            _voidResultSuccessCallback,
                                            _errorCallback,
                                            ByteOrder,
                                            _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteSingleHoldingRegisterWithUShortValueWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            const ushort value = 1234;

            // Act
            _sut.WriteSingleHoldingRegister(UnitIdentifier,
                                            RegisterAddress,
                                            value,
                                            _dispatcherMock.Object,
                                            _voidResultSuccessCallback,
                                            _errorCallback,
                                            ByteOrder,
                                            operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteSingleHoldingRegister));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteSingleHoldingRegisterAsync(UnitIdentifier,
                                                                                         RegisterAddress,
                                                                                         value,
                                                                                         ByteOrder,
                                                                                         operationTimeout,
                                                                                         CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersRawWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersRaw(UnitIdentifier,
                                                  StartingAddress,
                                                  [0xAA, 0xBB],
                                                  _dispatcherMock.Object,
                                                  _voidResultSuccessCallback,
                                                  _errorCallback,
                                                  _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersRawWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            byte[] values = [0x0A, 0x0B, 0x0C];

            // Act
            _sut.WriteMultipleHoldingRegistersRaw(UnitIdentifier,
                                                  StartingAddress,
                                                  values,
                                                  _dispatcherMock.Object,
                                                  _voidResultSuccessCallback,
                                                  _errorCallback,
                                                  operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersRaw));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersRawAsync(UnitIdentifier, StartingAddress, values, operationTimeout, CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersAsShortWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersAsShort(UnitIdentifier,
                                                      StartingAddress,
                                                      [-1, 2],
                                                      _dispatcherMock.Object,
                                                      _voidResultSuccessCallback,
                                                      _errorCallback,
                                                      ByteOrder,
                                                      _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersAsShortWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            short[] values = [-10, 20];

            // Act
            _sut.WriteMultipleHoldingRegistersAsShort(UnitIdentifier,
                                                      StartingAddress,
                                                      values,
                                                      _dispatcherMock.Object,
                                                      _voidResultSuccessCallback,
                                                      _errorCallback,
                                                      ByteOrder,
                                                      operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersAsShort));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersAsShortAsync(UnitIdentifier,
                                                                                                   StartingAddress,
                                                                                                   values,
                                                                                                   ByteOrder,
                                                                                                   operationTimeout,
                                                                                                   CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersAsUShortWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersAsUShort(UnitIdentifier,
                                                       StartingAddress,
                                                       [1, 2],
                                                       _dispatcherMock.Object,
                                                       _voidResultSuccessCallback,
                                                       _errorCallback,
                                                       ByteOrder,
                                                       _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersAsUShortWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            ushort[] values = [42, 84];

            // Act
            _sut.WriteMultipleHoldingRegistersAsUShort(UnitIdentifier,
                                                       StartingAddress,
                                                       values,
                                                       _dispatcherMock.Object,
                                                       _voidResultSuccessCallback,
                                                       _errorCallback,
                                                       ByteOrder,
                                                       operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersAsUShort));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersAsUShortAsync(UnitIdentifier,
                                                                                                    StartingAddress,
                                                                                                    values,
                                                                                                    ByteOrder,
                                                                                                    operationTimeout,
                                                                                                    CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersAsIntWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersAsInt(UnitIdentifier,
                                                    StartingAddress,
                                                    [-1000, 2000],
                                                    _dispatcherMock.Object,
                                                    _voidResultSuccessCallback,
                                                    _errorCallback,
                                                    ByteOrder,
                                                    WordOrder32,
                                                    _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersAsIntWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            int[] values = [-500, 500];

            // Act
            _sut.WriteMultipleHoldingRegistersAsInt(UnitIdentifier,
                                                    StartingAddress,
                                                    values,
                                                    _dispatcherMock.Object,
                                                    _voidResultSuccessCallback,
                                                    _errorCallback,
                                                    ByteOrder,
                                                    WordOrder32,
                                                    operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersAsInt));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersAsIntAsync(UnitIdentifier,
                                                                                                 StartingAddress,
                                                                                                 values,
                                                                                                 ByteOrder,
                                                                                                 WordOrder32,
                                                                                                 operationTimeout,
                                                                                                 CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersAsUIntWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersAsUInt(UnitIdentifier,
                                                     StartingAddress,
                                                     [1000u, 2000u],
                                                     _dispatcherMock.Object,
                                                     _voidResultSuccessCallback,
                                                     _errorCallback,
                                                     ByteOrder,
                                                     WordOrder32,
                                                     _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersAsUIntWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            uint[] values = [123u, 456u];

            // Act
            _sut.WriteMultipleHoldingRegistersAsUInt(UnitIdentifier,
                                                     StartingAddress,
                                                     values,
                                                     _dispatcherMock.Object,
                                                     _voidResultSuccessCallback,
                                                     _errorCallback,
                                                     ByteOrder,
                                                     WordOrder32,
                                                     operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersAsUInt));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersAsUIntAsync(UnitIdentifier,
                                                                                                  StartingAddress,
                                                                                                  values,
                                                                                                  ByteOrder,
                                                                                                  WordOrder32,
                                                                                                  operationTimeout,
                                                                                                  CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersAsFloatWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersAsFloat(UnitIdentifier,
                                                      StartingAddress,
                                                      [1.5f, -2.5f],
                                                      _dispatcherMock.Object,
                                                      _voidResultSuccessCallback,
                                                      _errorCallback,
                                                      ByteOrder,
                                                      WordOrder32,
                                                      _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersAsFloatWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            float[] values = [3.14f, -6.28f];

            // Act
            _sut.WriteMultipleHoldingRegistersAsFloat(UnitIdentifier,
                                                      StartingAddress,
                                                      values,
                                                      _dispatcherMock.Object,
                                                      _voidResultSuccessCallback,
                                                      _errorCallback,
                                                      ByteOrder,
                                                      WordOrder32,
                                                      operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersAsFloat));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersAsFloatAsync(UnitIdentifier,
                                                                                                   StartingAddress,
                                                                                                   values,
                                                                                                   ByteOrder,
                                                                                                   WordOrder32,
                                                                                                   operationTimeout,
                                                                                                   CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersAsLongWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersAsLong(UnitIdentifier,
                                                     StartingAddress,
                                                     [-123456789, 987654321],
                                                     _dispatcherMock.Object,
                                                     _voidResultSuccessCallback,
                                                     _errorCallback,
                                                     ByteOrder,
                                                     WordOrder64,
                                                     _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersAsLongWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            long[] values = [-1111111111, 2222222222];

            // Act
            _sut.WriteMultipleHoldingRegistersAsLong(UnitIdentifier,
                                                     StartingAddress,
                                                     values,
                                                     _dispatcherMock.Object,
                                                     _voidResultSuccessCallback,
                                                     _errorCallback,
                                                     ByteOrder,
                                                     WordOrder64,
                                                     operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersAsLong));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersAsLongAsync(UnitIdentifier,
                                                                                                  StartingAddress,
                                                                                                  values,
                                                                                                  ByteOrder,
                                                                                                  WordOrder64,
                                                                                                  operationTimeout,
                                                                                                  CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersAsULongWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersAsULong(UnitIdentifier,
                                                      StartingAddress,
                                                      [123456789UL, 987654321UL],
                                                      _dispatcherMock.Object,
                                                      _voidResultSuccessCallback,
                                                      _errorCallback,
                                                      ByteOrder,
                                                      WordOrder64,
                                                      _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersAsULongWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            ulong[] values = [1111111111UL, 2222222222UL];

            // Act
            _sut.WriteMultipleHoldingRegistersAsULong(UnitIdentifier,
                                                      StartingAddress,
                                                      values,
                                                      _dispatcherMock.Object,
                                                      _voidResultSuccessCallback,
                                                      _errorCallback,
                                                      ByteOrder,
                                                      WordOrder64,
                                                      operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersAsULong));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersAsULongAsync(UnitIdentifier,
                                                                                                   StartingAddress,
                                                                                                   values,
                                                                                                   ByteOrder,
                                                                                                   WordOrder64,
                                                                                                   operationTimeout,
                                                                                                   CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersAsDoubleWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersAsDouble(UnitIdentifier,
                                                       StartingAddress,
                                                       [1.234, 5.678],
                                                       _dispatcherMock.Object,
                                                       _voidResultSuccessCallback,
                                                       _errorCallback,
                                                       ByteOrder,
                                                       WordOrder64,
                                                       _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersAsDoubleWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            double[] values = [9.876, -5.432];

            // Act
            _sut.WriteMultipleHoldingRegistersAsDouble(UnitIdentifier,
                                                       StartingAddress,
                                                       values,
                                                       _dispatcherMock.Object,
                                                       _voidResultSuccessCallback,
                                                       _errorCallback,
                                                       ByteOrder,
                                                       WordOrder64,
                                                       operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Assert
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersAsDouble));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersAsDoubleAsync(UnitIdentifier,
                                                                                                    StartingAddress,
                                                                                                    values,
                                                                                                    ByteOrder,
                                                                                                    WordOrder64,
                                                                                                    operationTimeout,
                                                                                                    CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.2")]
        public void NotEnqueueWriteMultipleHoldingRegistersAsStringWhenClientDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            _sut.WriteMultipleHoldingRegistersAsString(UnitIdentifier,
                                                       StartingAddress,
                                                       "payload",
                                                       _dispatcherMock.Object,
                                                       _voidResultSuccessCallback,
                                                       _errorCallback,
                                                       TextEncoding,
                                                       _operationTimeout);

            // Assert
            VerifyVoidResultRequestNotEnqueued();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-001.2")]
        [DataRow(true, DisplayName = "With default operation timeout")]
        [DataRow(false, DisplayName = "With custom operation timeout")]
        public async Task EnqueueWriteMultipleHoldingRegistersAsStringWhenClientEnabled(bool useDefaultTimeout)
        {
            // Arrange
            _sut.IsEnabled = true;
            var operationTimeout = useDefaultTimeout ? _sut.DefaultOperationTimeout : _operationTimeout;
            const string value = "modbus";

            // Act
            _sut.WriteMultipleHoldingRegistersAsString(UnitIdentifier,
                                                       StartingAddress,
                                                       value,
                                                       _dispatcherMock.Object,
                                                       _voidResultSuccessCallback,
                                                       _errorCallback,
                                                       TextEncoding,
                                                       operationTimeout);
            await (_capturedOperation?.Invoke(CancellationToken.None) ?? Task.CompletedTask);

            // Arrange
            VerifyVoidResultRequestEnqueued(nameof(_sut.WriteMultipleHoldingRegistersAsString));
            _clientWrapperMock.Verify(wrapper => wrapper.WriteMultipleHoldingRegistersAsStringAsync(UnitIdentifier,
                                                                                                    StartingAddress,
                                                                                                    value,
                                                                                                    TextEncoding,
                                                                                                    operationTimeout,
                                                                                                    CancellationToken.None),
                                      Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-010.3")]
        public void ReleaseManagedResourcesWhenDisposed()
        {
            // Arrange

            // Act
            _sut.Dispose();

            // Assert
            _requestQueueMock.Verify(queue => queue.Dispose(), Times.Once);
            _clientWrapperMock.Verify(wrapper => wrapper.Dispose(), Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-010.3")]
        public void ReleaseManagedResourcesOnlyOnce()
        {
            // Arrange
            _sut.Dispose();
            _requestQueueMock.Invocations.Clear();
            _clientWrapperMock.Invocations.Clear();

            // Act
            _sut.Dispose();

            // Assert
            _requestQueueMock.Verify(queue => queue.Dispose(), Times.Never);
            _clientWrapperMock.Verify(wrapper => wrapper.Dispose(), Times.Never);
        }

        private static Action<T[], ModbusReceipt> ArrayResultSuccessCallback<T>()
            where T : unmanaged
        {
            return (_, _) => { };
        }

        private void SetupArrayResultOperationCapture<T>()
            where T : unmanaged
        {
            _requestQueueMock.Setup(queue => queue.Enqueue(It.IsAny<string>(),
                                                           It.IsAny<IActorDispatcher>(),
                                                           It.IsAny<Func<CancellationToken, Task<T[]>>>(),
                                                           It.IsAny<Action<T[], ModbusReceipt>>(),
                                                           It.IsAny<Action<Exception, ModbusReceipt>?>()))
                             .Callback<string, IActorDispatcher, Func<CancellationToken, Task<T[]>>, Action<T[], ModbusReceipt>, Action<Exception, ModbusReceipt>?>((_,
                                     _,
                                     operation,
                                     _,
                                     _) =>
                                 _capturedOperation = operation);
        }

        private void VerifyArrayResultRequestNotEnqueued<T>()
            where T : unmanaged
        {
            _requestQueueMock.Verify(queue => queue.Enqueue(It.IsAny<string>(),
                                                            It.IsAny<IActorDispatcher>(),
                                                            It.IsAny<Func<CancellationToken, Task<T[]>>>(),
                                                            It.IsAny<Action<T[], ModbusReceipt>>(),
                                                            It.IsAny<Action<Exception, ModbusReceipt>?>()),
                                     Times.Never);
        }

        private void VerifyArrayResultRequestEnqueued<T>(string requestName, Action<T[], ModbusReceipt> successCallback)
            where T : unmanaged
        {
            _requestQueueMock.Verify(queue => queue.Enqueue(requestName, _dispatcherMock.Object, It.IsAny<Func<CancellationToken, Task<T[]>>>(), successCallback, _errorCallback),
                                     Times.Once);
        }

        private void SetupSingleResultOperationCapture<T>()
        {
            _requestQueueMock.Setup(queue => queue.Enqueue(It.IsAny<string>(),
                                                           It.IsAny<IActorDispatcher>(),
                                                           It.IsAny<Func<CancellationToken, Task<T>>>(),
                                                           It.IsAny<Action<T, ModbusReceipt>>(),
                                                           It.IsAny<Action<Exception, ModbusReceipt>?>()))
                             .Callback<string, IActorDispatcher, Func<CancellationToken, Task<T>>, Action<T, ModbusReceipt>, Action<Exception, ModbusReceipt>?>((_,
                                     _,
                                     operation,
                                     _,
                                     _) =>
                                 _capturedOperation = operation);
        }

        private static Action<T, ModbusReceipt> SingleResultSuccessCallback<T>()
        {
            return (_, _) => { };
        }

        private void VerifySingleRequestResultNotEnqueued()
        {
            _requestQueueMock.Verify(queue => queue.Enqueue(It.IsAny<string>(),
                                                            It.IsAny<IActorDispatcher>(),
                                                            It.IsAny<Func<CancellationToken, Task<string>>>(),
                                                            It.IsAny<Action<string, ModbusReceipt>>(),
                                                            It.IsAny<Action<Exception, ModbusReceipt>?>()),
                                     Times.Never);
        }

        private void VerifySingleRequestResultEnqueued(string requestName, Action<string, ModbusReceipt> successCallback)
        {
            _requestQueueMock.Verify(queue => queue.Enqueue(requestName,
                                                            _dispatcherMock.Object,
                                                            It.IsAny<Func<CancellationToken, Task<string>>>(),
                                                            successCallback,
                                                            _errorCallback),
                                     Times.Once);
        }

        private void VerifyVoidResultRequestNotEnqueued()
        {
            _requestQueueMock.Verify(queue => queue.Enqueue(It.IsAny<string>(),
                                                            It.IsAny<IActorDispatcher>(),
                                                            It.IsAny<Func<CancellationToken, Task>>(),
                                                            It.IsAny<Action<ModbusReceipt>?>(),
                                                            It.IsAny<Action<Exception, ModbusReceipt>?>()),
                                     Times.Never);
        }

        private void VerifyVoidResultRequestEnqueued(string requestName)
        {
            _requestQueueMock.Verify(queue => queue.Enqueue(requestName,
                                                            _dispatcherMock.Object,
                                                            It.IsAny<Func<CancellationToken, Task>>(),
                                                            _voidResultSuccessCallback,
                                                            _errorCallback),
                                     Times.Once);
        }

        private void VerifyControlOperationNotEnqueued()
        {
            _requestQueueMock.Verify(queue => queue.EnqueueControlOperation(It.IsAny<string>(),
                                                                            It.IsAny<IActorDispatcher>(),
                                                                            It.IsAny<Func<CancellationToken, Task>>(),
                                                                            It.IsAny<Action?>(),
                                                                            It.IsAny<Action<Exception>?>()),
                                     Times.Never);
        }

        private void VerifyControlOperationEnqueued(string requestName)
        {
            _requestQueueMock.Verify(queue => queue.EnqueueControlOperation(requestName,
                                                                            _dispatcherMock.Object,
                                                                            It.IsAny<Func<CancellationToken, Task>>(),
                                                                            _controlSuccessCallback,
                                                                            _controlErrorCallback),
                                     Times.Once);
        }
    }
}