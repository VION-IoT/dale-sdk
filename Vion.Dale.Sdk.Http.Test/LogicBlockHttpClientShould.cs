using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Vion.Dale.Sdk.Http.Test
{
    [TestClass]
    public class LogicBlockHttpClientShould
    {
        private const string Url = "http://localhost";

        private readonly Mock<IActorDispatcher> _actorDispatcherMock = new();

        private readonly Action _callbackWithoutResponse = () => { };

        private readonly Action<TestObject> _callbackWithResponse = _ => { };

        private readonly Action<Exception> _errorCallback = _ => { };

        private readonly Dictionary<string, string> _headers = new() { { "Authorization", "Bearer token" } };

        private readonly Mock<ILogger<LogicBlockHttpClient>> _loggerMock = new();

        private readonly TestObject _requestBody = new() { StringValue = "test", IntValue = 42 };

        private readonly Mock<IHttpRequestExecutor> _requestExecutorMock = new();

        private readonly Action<HttpResponseMessage> _sendRequestCallback = _ => { };

        private readonly Mock<IHttpContentSerializer> _serializerMock = new();

        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

        private Func<HttpResponseMessage, Task<TestObject>> _capturedGetResponseContent =
            _ => Task.FromResult(new TestObject { StringValue = Guid.NewGuid().ToString(), IntValue = 1 });

        private LogicBlockHttpClient _sut = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _sut = new LogicBlockHttpClient(_requestExecutorMock.Object, _serializerMock.Object, _loggerMock.Object);
        }

        [TestMethod]
        public void ExecuteGetRequestWithCorrectParameters()
        {
            // Arrange

            // Act
            _sut.GetJson(_actorDispatcherMock.Object,
                         Url,
                         _callbackWithResponse,
                         _errorCallback,
                         _headers,
                         _timeout);

            // Assert
            _requestExecutorMock.Verify(requestExecutor => requestExecutor.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                                                               Url,
                                                                                               HttpMethod.Get,
                                                                                               It.IsAny<Func<HttpResponseMessage, Task<TestObject>>>(),
                                                                                               _callbackWithResponse,
                                                                                               _errorCallback,
                                                                                               _headers,
                                                                                               null,
                                                                                               _timeout),
                                        Times.Once);
        }

        [TestMethod]
        public async Task ExecuteGetRequestWithCorrectDeserializer()
        {
            // Arrange
            SetupCaptureGetResponseContent();
            _sut.GetJson(_actorDispatcherMock.Object,
                         Url,
                         _callbackWithResponse,
                         _errorCallback,
                         _headers,
                         _timeout);
            var responseContent = new HttpResponseMessage();

            // Act
            await _capturedGetResponseContent(responseContent);

            // Assert
            _serializerMock.Verify(serializer => serializer.DeserializeJsonAsync<TestObject>(responseContent.Content), Times.Once);
        }

        [TestMethod]
        public void ExecutePostRequestWithCorrectParameters()
        {
            // Arrange
            var expectedRequestContent = SetupSerializerToReturnContent();

            // Act
            _sut.PostJson(_actorDispatcherMock.Object,
                          Url,
                          _requestBody,
                          _callbackWithResponse,
                          _errorCallback,
                          _headers,
                          _timeout);

            // Assert
            _requestExecutorMock.Verify(requestExecutor => requestExecutor.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                                                               Url,
                                                                                               HttpMethod.Post,
                                                                                               It.IsAny<Func<HttpResponseMessage, Task<TestObject>>>(),
                                                                                               _callbackWithResponse,
                                                                                               _errorCallback,
                                                                                               _headers,
                                                                                               expectedRequestContent,
                                                                                               _timeout),
                                        Times.Once);
            _serializerMock.Verify(serializer => serializer.SerializeJson(_requestBody), Times.Once);
        }

        [TestMethod]
        public async Task ExecutePostRequestWithCorrectDeserializer()
        {
            // Arrange
            SetupCaptureGetResponseContent();
            _sut.PostJson(_actorDispatcherMock.Object,
                          Url,
                          _requestBody,
                          _callbackWithResponse,
                          _errorCallback,
                          _headers,
                          _timeout);
            var responseContent = new HttpResponseMessage();

            // Act
            await _capturedGetResponseContent(responseContent);

            // Assert
            _serializerMock.Verify(serializer => serializer.DeserializeJsonAsync<TestObject>(responseContent.Content), Times.Once);
        }

        [TestMethod]
        public void ExecutePostRequestWithoutResponseWithCorrectParameters()
        {
            // Arrange
            var expectedRequestContent = SetupSerializerToReturnContent();

            // Act
            _sut.PostJson(_actorDispatcherMock.Object,
                          Url,
                          _requestBody,
                          _callbackWithoutResponse,
                          _errorCallback,
                          _headers,
                          _timeout);

            // Assert
            _requestExecutorMock.Verify(requestExecutor => requestExecutor.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                                                               Url,
                                                                                               HttpMethod.Post,
                                                                                               _callbackWithoutResponse,
                                                                                               _errorCallback,
                                                                                               _headers,
                                                                                               expectedRequestContent,
                                                                                               _timeout),
                                        Times.Once);
            _serializerMock.Verify(serializer => serializer.SerializeJson(_requestBody), Times.Once);
        }

        [TestMethod]
        public void ExecutePutRequestWithCorrectParameters()
        {
            // Arrange
            var expectedRequestContent = SetupSerializerToReturnContent();

            // Act
            _sut.PutJson(_actorDispatcherMock.Object,
                         Url,
                         _requestBody,
                         _callbackWithResponse,
                         _errorCallback,
                         _headers,
                         _timeout);

            // Assert
            _requestExecutorMock.Verify(requestExecutor => requestExecutor.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                                                               Url,
                                                                                               HttpMethod.Put,
                                                                                               It.IsAny<Func<HttpResponseMessage, Task<TestObject>>>(),
                                                                                               _callbackWithResponse,
                                                                                               _errorCallback,
                                                                                               _headers,
                                                                                               expectedRequestContent,
                                                                                               _timeout),
                                        Times.Once);
            _serializerMock.Verify(serializer => serializer.SerializeJson(_requestBody), Times.Once);
        }

        [TestMethod]
        public async Task ExecutePutRequestWithCorrectDeserializer()
        {
            // Arrange
            SetupCaptureGetResponseContent();
            _sut.PutJson(_actorDispatcherMock.Object,
                         Url,
                         _requestBody,
                         _callbackWithResponse,
                         _errorCallback,
                         _headers,
                         _timeout);
            var responseContent = new HttpResponseMessage();

            // Act
            await _capturedGetResponseContent(responseContent);

            // Assert
            _serializerMock.Verify(serializer => serializer.DeserializeJsonAsync<TestObject>(responseContent.Content), Times.Once);
        }

        [TestMethod]
        public void ExecutePutRequestWithoutResponseWithCorrectParameters()
        {
            // Arrange
            var expectedRequestContent = SetupSerializerToReturnContent();

            // Act
            _sut.PutJson(_actorDispatcherMock.Object,
                         Url,
                         _requestBody,
                         _callbackWithoutResponse,
                         _errorCallback,
                         _headers,
                         _timeout);

            // Assert
            _requestExecutorMock.Verify(requestExecutor => requestExecutor.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                                                               Url,
                                                                                               HttpMethod.Put,
                                                                                               _callbackWithoutResponse,
                                                                                               _errorCallback,
                                                                                               _headers,
                                                                                               expectedRequestContent,
                                                                                               _timeout),
                                        Times.Once);
            _serializerMock.Verify(serializer => serializer.SerializeJson(_requestBody), Times.Once);
        }

        [TestMethod]
        public void ExecuteDeleteRequestWithCorrectParameters()
        {
            // Arrange

            // Act
            _sut.DeleteJson(_actorDispatcherMock.Object,
                            Url,
                            _callbackWithResponse,
                            _errorCallback,
                            _headers,
                            _timeout);

            // Assert
            _requestExecutorMock.Verify(requestExecutor => requestExecutor.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                                                               Url,
                                                                                               HttpMethod.Delete,
                                                                                               It.IsAny<Func<HttpResponseMessage, Task<TestObject>>>(),
                                                                                               _callbackWithResponse,
                                                                                               _errorCallback,
                                                                                               _headers,
                                                                                               null,
                                                                                               _timeout),
                                        Times.Once);
        }

        [TestMethod]
        public async Task ExecuteDeleteRequestWithCorrectDeserializer()
        {
            // Arrange
            SetupCaptureGetResponseContent();
            _sut.DeleteJson(_actorDispatcherMock.Object,
                            Url,
                            _callbackWithResponse,
                            _errorCallback,
                            _headers,
                            _timeout);
            var responseContent = new HttpResponseMessage();

            // Act
            await _capturedGetResponseContent(responseContent);

            // Assert
            _serializerMock.Verify(serializer => serializer.DeserializeJsonAsync<TestObject>(responseContent.Content), Times.Once);
        }

        [TestMethod]
        public void ExecuteDeleteRequestWithoutResponseWithCorrectParameters()
        {
            // Arrange

            // Act
            _sut.Delete(_actorDispatcherMock.Object,
                        Url,
                        _callbackWithoutResponse,
                        _errorCallback,
                        _headers,
                        _timeout);

            // Assert
            _requestExecutorMock.Verify(requestExecutor => requestExecutor.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                                                               Url,
                                                                                               HttpMethod.Delete,
                                                                                               _callbackWithoutResponse,
                                                                                               _errorCallback,
                                                                                               _headers,
                                                                                               null,
                                                                                               _timeout),
                                        Times.Once);
        }

        [TestMethod]
        public void ExecuteSendRequestWithCorrectParameters()
        {
            // Arrange
            var request = new HttpRequestMessage(HttpMethod.Patch, Url);

            // Act
            _sut.SendRequest(_actorDispatcherMock.Object, request, _sendRequestCallback, _errorCallback, _timeout);

            // Assert
            _requestExecutorMock.Verify(requestExecutor => requestExecutor.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                                                               request,
                                                                                               _sendRequestCallback,
                                                                                               _errorCallback,
                                                                                               _timeout),
                                        Times.Once);
        }

        private void SetupCaptureGetResponseContent()
        {
            _requestExecutorMock
                .Setup(requestExecutor => requestExecutor.ExecuteRequestAsync(It.IsAny<IActorDispatcher>(),
                                                                              It.IsAny<string>(),
                                                                              It.IsAny<HttpMethod>(),
                                                                              It.IsAny<Func<HttpResponseMessage, Task<TestObject>>>(),
                                                                              It.IsAny<Action<TestObject>>(),
                                                                              It.IsAny<Action<Exception>>(),
                                                                              It.IsAny<Dictionary<string, string>>(),
                                                                              It.IsAny<HttpContent>(),
                                                                              It.IsAny<TimeSpan?>()))
                .Callback<IActorDispatcher, string, HttpMethod, Func<HttpResponseMessage, Task<TestObject>>, Action<TestObject>, Action<Exception>, Dictionary<string, string>,
                    HttpContent, TimeSpan?>((_,
                                             _,
                                             _,
                                             deserializer,
                                             _,
                                             _,
                                             _,
                                             _,
                                             _) => _capturedGetResponseContent = deserializer)
                .Returns(Task.CompletedTask);
        }

        private StringContent SetupSerializerToReturnContent()
        {
            var serializedContent = new StringContent("serialized");
            _serializerMock.Setup(serializer => serializer.SerializeJson(_requestBody)).Returns(serializedContent);

            return serializedContent;
        }
    }
}