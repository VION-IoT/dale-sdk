using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vion.Contracts.Constants;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;
using Google.FlatBuffers;
using Microsoft.Extensions.Logging;
using Moq;

namespace Vion.Dale.Sdk.Modbus.Rtu.Test
{
    [TestClass]
    public class ModbusRtuHandlerShould
    {
        private const string ContractIdentifier = "rtu0";

        private const string LogicBlockIdValue = "lb0";

        private const string ServiceProviderIdentifier = "sp0";

        private const string ServiceIdentifier = "svc0";

        private const string UnlinkedLogicBlockIdValue = "lb-unlinked";

        private const ModbusFunctionCode ReadFunctionCode = ModbusFunctionCode.ReadHoldingRegisters;

        private const ModbusFunctionCode WriteFunctionCode = ModbusFunctionCode.WriteMultipleRegisters;

        private const byte UnitId = 7;

        private const ushort StartingAddress = 10;

        private const ushort Quantity = 4;

        private const ushort WriteAddress = 20;

        private const string GetRequestTopic = $"installation/{ServiceProviderIdentifier}/{ServiceIdentifier}/{ContractIdentifier}/hw/modbus/get";

        private const string SetRequestTopic = $"installation/{ServiceProviderIdentifier}/{ServiceIdentifier}/{ContractIdentifier}/hw/modbus/set";

        private const string GetResponseTopic = $"{GetRequestTopic}/{ServiceProviderConstants.DaleIdentifier}/response";

        private const string SetResponseTopic = $"{SetRequestTopic}/{ServiceProviderConstants.DaleIdentifier}/response";

        private static readonly LogicBlockContractId LinkedLogicBlockContractId = new(new LogicBlockId(LogicBlockIdValue), ContractIdentifier);

        private static readonly LogicBlockContractId UnlinkedLogicBlockContractId = new(new LogicBlockId(UnlinkedLogicBlockIdValue), ContractIdentifier);

        private static readonly ServiceProviderContractId SpContractId = new(ServiceProviderIdentifier, ServiceIdentifier, ContractIdentifier);

        private static readonly DateTime Now = new(2026,
                                                   1,
                                                   1,
                                                   0,
                                                   0,
                                                   0,
                                                   DateTimeKind.Utc);

        private static readonly DateTime FutureExpiration = Now.AddMinutes(1);

        private static readonly DateTime PastExpiration = Now.AddMinutes(-1);

        private static readonly byte[] ResponseData = [0x01, 0x02, 0x03, 0x04];

        private static readonly byte[] WriteData = [0xAA, 0xBB];

        public enum TargetMethod
        {
            Read,

            Write,
        }

        private readonly Mock<IActorContext> _actorContextMock = new();

        private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();

        private readonly Mock<ILogger<ModbusRtuHandler>> _loggerMock = new();

        private readonly Mock<IActorReference> _logicBlockActorRefMock = new();

        private readonly Mock<IActorReference> _mqttClientActorRefMock = new();

        private TimeSpan? _scheduledExpirationDelay;

        private object? _scheduledExpirationMessage;

        private ModbusRtuHandler _sut = null!;

        [TestInitialize]
        public async Task InitializeAsync()
        {
            _dateTimeProviderMock.Setup(provider => provider.UtcNow).Returns(Now);
            _actorContextMock.Setup(actorContext => actorContext.LookupByName(MqttConstants.MqttClientName)).Returns(_mqttClientActorRefMock.Object);
            _actorContextMock.Setup(actorContext => actorContext.SendToSelfAfter(It.IsAny<object>(), It.IsAny<TimeSpan>()))
                             .Callback<object, TimeSpan>((message, delay) =>
                                                         {
                                                             _scheduledExpirationMessage = message;
                                                             _scheduledExpirationDelay = delay;
                                                         });
            _sut = new ModbusRtuHandler(_dateTimeProviderMock.Object, _loggerMock.Object);
            await LinkAsync((SpContractId, LinkedLogicBlockContractId, _logicBlockActorRefMock.Object));
            _actorContextMock.Invocations.Clear();
        }

        [TestMethod]
        [DataRow(TargetMethod.Read)]
        [DataRow(TargetMethod.Write)]
        public async Task PublishRequestToMqttClient(TargetMethod targetMethod)
        {
            // Arrange

            // Act
            await SendRequestAsync(targetMethod);

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_mqttClientActorRefMock.Object,
                                                                        It.Is<object>(message => message is PublishMqttMessage),
                                                                        It.IsAny<Dictionary<string, string>?>()),
                                     Times.Once);
        }

        [TestMethod]
        public async Task PublishReadRequestToCorrectTopic()
        {
            // Arrange

            // Act
            await SendRequestAsync(TargetMethod.Read);

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_mqttClientActorRefMock.Object,
                                                                        It.Is<object>(message => ((PublishMqttMessage)message).Topic == GetRequestTopic &&
                                                                                                 ((PublishMqttMessage)message).ResponseTopic == GetResponseTopic),
                                                                        It.IsAny<Dictionary<string, string>?>()),
                                     Times.Once);
        }

        [TestMethod]
        public async Task PublishWriteRequestToCorrectTopic()
        {
            // Arrange

            // Act
            await SendRequestAsync(TargetMethod.Write);

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_mqttClientActorRefMock.Object,
                                                                        It.Is<object>(message => ((PublishMqttMessage)message).Topic == SetRequestTopic &&
                                                                                                 ((PublishMqttMessage)message).ResponseTopic == SetResponseTopic),
                                                                        It.IsAny<Dictionary<string, string>?>()),
                                     Times.Once);
        }

        [TestMethod]
        [DataRow(TargetMethod.Read)]
        [DataRow(TargetMethod.Write)]
        public async Task DropRequestWhenContractNotMapped(TargetMethod targetMethod)
        {
            // Arrange

            // Act
            await SendRequestAsync(targetMethod, UnlinkedLogicBlockContractId);

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        public async Task SendTimeoutReadResponseWhenRequestAlreadyExpired()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            byte[]? capturedData = null;
            var request = new ReadModbusRtuRequest(ReadFunctionCode,
                                                   UnitId,
                                                   StartingAddress,
                                                   Quantity,
                                                   Now,
                                                   PastExpiration,
                                                   correlationId,
                                                   (data, exception) =>
                                                   {
                                                       capturedData = data;
                                                       capturedException = exception;
                                                   });

            // Act
            await DispatchAsync(new ContractMessage<ReadModbusRtuRequest>(LinkedLogicBlockContractId, request));
            InvokeCapturedReadResponseCallback();

            // Assert
            Assert.IsInstanceOfType<OperationTimeoutException>(capturedException);
            Assert.IsNull(capturedData);
        }

        [TestMethod]
        public async Task SendTimeoutWriteResponseWhenRequestAlreadyExpired()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            var request = new WriteModbusRtuRequest(WriteFunctionCode,
                                                    UnitId,
                                                    WriteAddress,
                                                    WriteData,
                                                    Now,
                                                    PastExpiration,
                                                    correlationId,
                                                    exception => capturedException = exception);

            // Act
            await DispatchAsync(new ContractMessage<WriteModbusRtuRequest>(LinkedLogicBlockContractId, request));
            InvokeCapturedWriteResponseCallback();

            // Assert
            Assert.IsInstanceOfType<OperationTimeoutException>(capturedException);
        }

        [TestMethod]
        public async Task SendLimitReachedReadResponseWhenPendingRequestLimitReached()
        {
            // Arrange
            for (var i = 0; i < ModbusRtuHandler.MaxPendingRequests; i++)
            {
                await SendRequestAsync(TargetMethod.Read);
            }

            _actorContextMock.Invocations.Clear();
            Exception? capturedException = null;

            // Act
            await SendReadRequestAsync(Guid.NewGuid(), (_, exception) => capturedException = exception);
            InvokeCapturedReadResponseCallback();

            // Assert
            Assert.IsInstanceOfType<PendingRequestsLimitReachedException>(capturedException);
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_mqttClientActorRefMock.Object, It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        public async Task SendLimitReachedWriteResponseWhenPendingRequestLimitReached()
        {
            // Arrange
            for (var i = 0; i < ModbusRtuHandler.MaxPendingRequests; i++)
            {
                await SendRequestAsync(TargetMethod.Write);
            }

            _actorContextMock.Invocations.Clear();
            Exception? capturedException = null;

            // Act
            await SendWriteRequestAsync(Guid.NewGuid(), exception => capturedException = exception);
            InvokeCapturedWriteResponseCallback();

            // Assert
            Assert.IsInstanceOfType<PendingRequestsLimitReachedException>(capturedException);
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_mqttClientActorRefMock.Object, It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        public async Task InvokeReadCallbackWithDataWhenOkResponseReceived()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            byte[]? capturedData = null;
            await SendReadRequestAsync(correlationId,
                                       (data, exception) =>
                                       {
                                           capturedData = data;
                                           capturedException = exception;
                                       });

            // Act
            await DispatchAsync(CreateGetResponseMqttMessage(correlationId, ModbusResponseCode.Ok, ResponseData));
            InvokeCapturedReadResponseCallback();

            // Assert
            CollectionAssert.AreEqual(ResponseData, capturedData);
            Assert.IsNull(capturedException);
        }

        [TestMethod]
        public async Task InvokeReadCallbackWithModbusExceptionWhenErrorResponseReceived()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            byte[]? capturedData = null;
            await SendReadRequestAsync(correlationId,
                                       (data, exception) =>
                                       {
                                           capturedData = data;
                                           capturedException = exception;
                                       });

            // Act
            await DispatchAsync(CreateGetResponseMqttMessage(correlationId, ModbusResponseCode.ServerDeviceFailure, ResponseData, "boom"));
            InvokeCapturedReadResponseCallback();

            // Assert
            Assert.IsInstanceOfType<ModbusException>(capturedException);
            Assert.IsNull(capturedData);
        }

        [TestMethod]
        public async Task InvokeReadCallbackWithExceptionWhenResponsePayloadIsInvalid()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            await SendReadRequestAsync(correlationId, (_, exception) => capturedException = exception);

            // Act
            await DispatchAsync(CreateMqttMessage(GetResponseTopic, [0x01, 0x02, 0x03], correlationId));
            InvokeCapturedReadResponseCallback();

            // Assert
            Assert.IsNotNull(capturedException);
            Assert.IsNotInstanceOfType<ModbusException>(capturedException);
        }

        [TestMethod]
        public async Task IgnoreReadResponseWhenNoPendingRequest()
        {
            // Arrange

            // Act
            await DispatchAsync(CreateGetResponseMqttMessage(Guid.NewGuid(), ModbusResponseCode.Ok, ResponseData));

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        public async Task InvokeWriteCallbackWhenOkResponseReceived()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            var callbackInvoked = false;
            await SendWriteRequestAsync(correlationId,
                                        exception =>
                                        {
                                            callbackInvoked = true;
                                            capturedException = exception;
                                        });

            // Act
            await DispatchAsync(CreateSetResponseMqttMessage(correlationId, ModbusResponseCode.Ok));
            InvokeCapturedWriteResponseCallback();

            // Assert
            Assert.IsTrue(callbackInvoked);
            Assert.IsNull(capturedException);
        }

        [TestMethod]
        public async Task InvokeWriteCallbackWithModbusExceptionWhenErrorResponseReceived()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            await SendWriteRequestAsync(correlationId, exception => capturedException = exception);

            // Act
            await DispatchAsync(CreateSetResponseMqttMessage(correlationId, ModbusResponseCode.ServerDeviceFailure, "boom"));
            InvokeCapturedWriteResponseCallback();

            // Assert
            Assert.IsInstanceOfType<ModbusException>(capturedException);
        }

        [TestMethod]
        public async Task InvokeWriteCallbackWithExceptionWhenResponsePayloadIsInvalid()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            await SendWriteRequestAsync(correlationId, exception => capturedException = exception);

            // Act
            await DispatchAsync(CreateMqttMessage(SetResponseTopic, [0x01, 0x02, 0x03], correlationId));
            InvokeCapturedWriteResponseCallback();

            // Assert
            Assert.IsNotNull(capturedException);
            Assert.IsNotInstanceOfType<ModbusException>(capturedException);
        }

        [TestMethod]
        public async Task IgnoreWriteResponseWhenNoPendingRequest()
        {
            // Arrange

            // Act
            await DispatchAsync(CreateSetResponseMqttMessage(Guid.NewGuid(), ModbusResponseCode.Ok));

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        public async Task IgnoreMqttMessageWhenCorrelationIdIsEmpty()
        {
            // Arrange

            // Act
            await DispatchAsync(CreateMqttMessage(GetResponseTopic, [], Guid.Empty));

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        public async Task IgnoreUnknownMqttTopic()
        {
            // Arrange
            var unknownTopic = $"installation/{ServiceProviderIdentifier}/{ServiceIdentifier}/{ContractIdentifier}/something/else";

            // Act
            await DispatchAsync(CreateMqttMessage(unknownTopic, [], Guid.NewGuid()));

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
        }

        [TestMethod]
        public void ScheduleExpirationCheckWhenLinked()
        {
            // Arrange

            // Act

            // Assert
            Assert.IsNotNull(_scheduledExpirationMessage);
            Assert.AreEqual(TimeSpan.FromSeconds(1), _scheduledExpirationDelay);
        }

        [TestMethod]
        public async Task CompleteExpiredReadRequestWithTimeout()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            await SendReadRequestAsync(correlationId, (_, exception) => capturedException = exception);
            _dateTimeProviderMock.Setup(provider => provider.UtcNow).Returns(FutureExpiration.AddSeconds(1));

            // Act
            await TriggerExpirationCheckAsync();
            InvokeCapturedReadResponseCallback();

            // Assert
            Assert.IsInstanceOfType<OperationTimeoutException>(capturedException);
        }

        [TestMethod]
        public async Task CompleteExpiredWriteRequestWithTimeout()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            Exception? capturedException = null;
            await SendWriteRequestAsync(correlationId, exception => capturedException = exception);
            _dateTimeProviderMock.Setup(provider => provider.UtcNow).Returns(FutureExpiration.AddSeconds(1));

            // Act
            await TriggerExpirationCheckAsync();
            InvokeCapturedWriteResponseCallback();

            // Assert
            Assert.IsInstanceOfType<OperationTimeoutException>(capturedException);
        }

        [TestMethod]
        public async Task NotCompleteNonExpiredReadRequest()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            var callbackInvoked = false;
            await SendReadRequestAsync(correlationId, (_, _) => callbackInvoked = true);
            _actorContextMock.Invocations.Clear();

            // Act
            await TriggerExpirationCheckAsync();

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_logicBlockActorRefMock.Object, It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
            Assert.IsFalse(callbackInvoked);
        }

        [TestMethod]
        public async Task NotCompleteNonExpiredWriteRequest()
        {
            // Arrange
            var correlationId = Guid.NewGuid();
            var callbackInvoked = false;
            await SendWriteRequestAsync(correlationId, _ => callbackInvoked = true);
            _actorContextMock.Invocations.Clear();

            // Act
            await TriggerExpirationCheckAsync();

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_logicBlockActorRefMock.Object, It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()), Times.Never);
            Assert.IsFalse(callbackInvoked);
        }

        [TestMethod]
        public async Task RescheduleExpirationCheckAfterRun()
        {
            // Arrange

            // Act
            await TriggerExpirationCheckAsync();

            // Assert
            Assert.IsNotNull(_scheduledExpirationMessage);
            Assert.AreEqual(TimeSpan.FromSeconds(1), _scheduledExpirationDelay);
        }

        [TestMethod]
        public async Task NotRescheduleExpirationCheckOnSubsequentLink()
        {
            // Arrange
            _scheduledExpirationMessage = null;
            _scheduledExpirationDelay = null;

            // Act
            await LinkAsync((SpContractId, LinkedLogicBlockContractId, _logicBlockActorRefMock.Object));

            // Assert
            Assert.IsNull(_scheduledExpirationMessage);
            Assert.IsNull(_scheduledExpirationDelay);
        }

        [TestMethod]
        public async Task StoreNewServiceProviderMappingForUnlinkedLogicBlock()
        {
            // Arrange
            var newActorRefMock = new Mock<IActorReference>();
            await LinkAsync((SpContractId, UnlinkedLogicBlockContractId, newActorRefMock.Object));

            // Act
            await SendRequestAsync(TargetMethod.Read, UnlinkedLogicBlockContractId);

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_mqttClientActorRefMock.Object,
                                                                        It.Is<object>(message => message is PublishMqttMessage),
                                                                        It.IsAny<Dictionary<string, string>?>()),
                                     Times.Once);
        }

        [TestMethod]
        public async Task NotOverwriteExistingServiceProviderMappingOnRelink()
        {
            // Arrange
            var otherSpContractId = new ServiceProviderContractId("other-sp", ServiceIdentifier, ContractIdentifier);
            await LinkAsync((otherSpContractId, LinkedLogicBlockContractId, _logicBlockActorRefMock.Object));

            // Act
            await SendRequestAsync(TargetMethod.Read);

            // Assert
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_mqttClientActorRefMock.Object,
                                                                        It.Is<object>(message => ((PublishMqttMessage)message).Topic == GetRequestTopic),
                                                                        It.IsAny<Dictionary<string, string>?>()),
                                     Times.Once);
        }

        private Task DispatchAsync(object message)
        {
            return ((IActorReceiver)_sut).HandleMessageAsync(message, _actorContextMock.Object);
        }

        private Task LinkAsync(params (ServiceProviderContractId ServiceProviderContractId, LogicBlockContractId LogicBlockContractId, IActorReference ActorReference)[] mappings)
        {
            var outer = new Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>>();
            foreach (var (serviceProviderContractId, logicBlockContractId, actorReference) in mappings)
            {
                if (!outer.TryGetValue(serviceProviderContractId, out var inner))
                {
                    inner = new Dictionary<LogicBlockContractId, IActorReference>();
                    outer[serviceProviderContractId] = inner;
                }

                inner[logicBlockContractId] = actorReference;
            }

            return DispatchAsync(new LinkLogicBlockContractActors(outer));
        }

        private Task SendRequestAsync(TargetMethod targetMethod, LogicBlockContractId? logicBlockContractId = null)
        {
            var contractId = logicBlockContractId ?? LinkedLogicBlockContractId;
            var correlationId = Guid.NewGuid();
            switch (targetMethod)
            {
                case TargetMethod.Read:
                    var readRequest = new ReadModbusRtuRequest(ReadFunctionCode,
                                                               UnitId,
                                                               StartingAddress,
                                                               Quantity,
                                                               Now,
                                                               FutureExpiration,
                                                               correlationId,
                                                               (_, _) => { });
                    return DispatchAsync(new ContractMessage<ReadModbusRtuRequest>(contractId, readRequest));
                case TargetMethod.Write:
                    var writeRequest = new WriteModbusRtuRequest(WriteFunctionCode,
                                                                 UnitId,
                                                                 WriteAddress,
                                                                 WriteData,
                                                                 Now,
                                                                 FutureExpiration,
                                                                 correlationId,
                                                                 _ => { });
                    return DispatchAsync(new ContractMessage<WriteModbusRtuRequest>(contractId, writeRequest));
                default: throw new ArgumentOutOfRangeException(nameof(targetMethod), targetMethod, null);
            }
        }

        private Task SendReadRequestAsync(Guid correlationId, Action<byte[]?, Exception?> callback)
        {
            var request = new ReadModbusRtuRequest(ReadFunctionCode,
                                                   UnitId,
                                                   StartingAddress,
                                                   Quantity,
                                                   Now,
                                                   FutureExpiration,
                                                   correlationId,
                                                   callback);

            return DispatchAsync(new ContractMessage<ReadModbusRtuRequest>(LinkedLogicBlockContractId, request));
        }

        private Task SendWriteRequestAsync(Guid correlationId, Action<Exception?> callback)
        {
            var request = new WriteModbusRtuRequest(WriteFunctionCode,
                                                    UnitId,
                                                    WriteAddress,
                                                    WriteData,
                                                    Now,
                                                    FutureExpiration,
                                                    correlationId,
                                                    callback);

            return DispatchAsync(new ContractMessage<WriteModbusRtuRequest>(LinkedLogicBlockContractId, request));
        }

        private Task TriggerExpirationCheckAsync()
        {
            var message = _scheduledExpirationMessage!;
            _scheduledExpirationMessage = null;
            _scheduledExpirationDelay = null;

            return DispatchAsync(message);
        }

        private void InvokeCapturedReadResponseCallback()
        {
            object? capturedMessage = null;
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_logicBlockActorRefMock.Object,
                                                                        It.Is<object>(message => CaptureAndMatch(message, ref capturedMessage, m => m is ContractMessage<ReadModbusRtuResponse>)),
                                                                        It.IsAny<Dictionary<string, string>?>()),
                                     Times.AtLeastOnce);
            var response = ((ContractMessage<ReadModbusRtuResponse>)capturedMessage!).Data;
            response.Callback(response.Data, response.Exception);
        }

        private void InvokeCapturedWriteResponseCallback()
        {
            object? capturedMessage = null;
            _actorContextMock.Verify(actorContext => actorContext.SendTo(_logicBlockActorRefMock.Object,
                                                                        It.Is<object>(message => CaptureAndMatch(message, ref capturedMessage, m => m is ContractMessage<WriteModbusRtuResponse>)),
                                                                        It.IsAny<Dictionary<string, string>?>()),
                                     Times.AtLeastOnce);
            var response = ((ContractMessage<WriteModbusRtuResponse>)capturedMessage!).Data;
            response.Callback(response.Exception);
        }

        private static bool CaptureAndMatch(object message, ref object? captured, Func<object, bool> predicate)
        {
            if (!predicate(message))
            {
                return false;
            }

            captured = message;

            return true;
        }

        private static MqttMessageReceived CreateGetResponseMqttMessage(Guid correlationId, ModbusResponseCode responseCode, byte[] data, string errorMessage = "")
        {
            var builder = new FlatBufferBuilder(32 + data.Length + errorMessage.Length);
            var errorMessageOffset = builder.CreateString(errorMessage);
            var dataOffset = GetModbusResponsePayload.CreateDataVector(builder, data);
            var payloadOffset = GetModbusResponsePayload.CreateGetModbusResponsePayload(builder, responseCode, errorMessageOffset, dataOffset);
            GetModbusResponsePayload.FinishGetModbusResponsePayloadBuffer(builder, payloadOffset);

            return CreateMqttMessage(GetResponseTopic, builder.SizedByteArray(), correlationId);
        }

        private static MqttMessageReceived CreateSetResponseMqttMessage(Guid correlationId, ModbusResponseCode responseCode, string errorMessage = "")
        {
            var builder = new FlatBufferBuilder(32 + errorMessage.Length);
            var errorMessageOffset = builder.CreateString(errorMessage);
            var payloadOffset = SetModbusResponsePayload.CreateSetModbusResponsePayload(builder, responseCode, errorMessageOffset);
            SetModbusResponsePayload.FinishSetModbusResponsePayloadBuffer(builder, payloadOffset);

            return CreateMqttMessage(SetResponseTopic, builder.SizedByteArray(), correlationId);
        }

        private static MqttMessageReceived CreateMqttMessage(string topic, byte[] payload, Guid correlationId)
        {
            return new MqttMessageReceived(topic,
                                           new ReadOnlySequence<byte>(payload),
                                           correlationId == Guid.Empty ? null : correlationId.ToByteArray(),
                                           null,
                                           []);
        }
    }
}
