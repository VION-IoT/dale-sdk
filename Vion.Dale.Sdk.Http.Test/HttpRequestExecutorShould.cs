using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Vion.Dale.Sdk.Http.Test
{
    [TestClass]
    public class HttpRequestExecutorShould
    {
        private const string Url = "http://localhost";

        public enum TargetMethod
        {
            ResponseContent,

            NoResponse,

            ResponseMessage,
        }

        private readonly Mock<IActorDispatcher> _actorDispatcherMock = new();

        private readonly Mock<IHttpClientFactory> _clientFactoryMock = new();

        private readonly Dictionary<string, string> _headers = new()
                                                               {
                                                                   { "Authorization", "Bearer token" },
                                                                   { "Test", Guid.NewGuid().ToString() },
                                                               };

        private readonly Mock<ILogger<HttpRequestExecutor>> _loggerMock = new();

        private readonly string _requestContent = JsonSerializer.Serialize(new TestObject { StringValue = Guid.NewGuid().ToString(), IntValue = 2 });

        private readonly string _responseContent = JsonSerializer.Serialize(new TestObject { StringValue = Guid.NewGuid().ToString(), IntValue = 1 });

        private Exception? _actualException;

        private string? _actualRequestContent;

        private HttpRequestHeaders? _actualRequestHeaders;

        private HttpMethod? _actualRequestMethod;

        private Uri? _actualRequestUri;

        private string? _actualResponseContent;

        private HttpResponseMessage? _actualResponseMessage;

        private Action _capturedInvokeSynchronized = () => { };

        private bool _successCallbackInvoked;

        private HttpRequestExecutor _sut = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _sut = new HttpRequestExecutor(_clientFactoryMock.Object, _loggerMock.Object);
            SetupCaptureInvokeSynchronized();
        }

        [TestMethod]
        [DataRow(TargetMethod.ResponseContent, DisplayName = "for Action<TContent> callback overload")]
        [DataRow(TargetMethod.NoResponse, DisplayName = "for Action callback overload")]
        [DataRow(TargetMethod.ResponseMessage, DisplayName = "for Action<HttpResponseMessage> callback overload")]
        public async Task SendRequestWithCorrectUri(TargetMethod targetMethod)
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.OK);

            // Act
            await ExecuteRequestAsync(targetMethod, Url, HttpMethod.Get);

            // Assert
            Assert.IsNotNull(_actualRequestUri);
            Assert.AreEqual(Url, _actualRequestUri.OriginalString);
        }

        [TestMethod]
        [DataRow("GET", TargetMethod.ResponseContent)]
        [DataRow("GET", TargetMethod.NoResponse)]
        [DataRow("GET", TargetMethod.ResponseMessage)]
        [DataRow("POST", TargetMethod.ResponseContent)]
        [DataRow("POST", TargetMethod.NoResponse)]
        [DataRow("POST", TargetMethod.ResponseMessage)]
        [DataRow("PUT", TargetMethod.ResponseContent)]
        [DataRow("PUT", TargetMethod.NoResponse)]
        [DataRow("PUT", TargetMethod.ResponseMessage)]
        [DataRow("DELETE", TargetMethod.ResponseContent)]
        [DataRow("DELETE", TargetMethod.NoResponse)]
        [DataRow("DELETE", TargetMethod.ResponseMessage)]
        public async Task SendRequestWithCorrectHttpMethod(string httpMethodString, TargetMethod targetMethod)
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.OK);
            var httpMethod = GetHttpMethod(httpMethodString);

            // Act
            await ExecuteRequestAsync(targetMethod, Url, httpMethod);

            // Assert
            Assert.IsNotNull(_actualRequestMethod);
            Assert.AreEqual(httpMethod.Method, _actualRequestMethod.Method);
        }

        [TestMethod]
        [DataRow(TargetMethod.ResponseContent, DisplayName = "for Action<TContent> callback overload")]
        [DataRow(TargetMethod.NoResponse, DisplayName = "for Action callback overload")]
        [DataRow(TargetMethod.ResponseMessage, DisplayName = "for Action<HttpResponseMessage> callback overload")]
        public async Task SendRequestWithCorrectContent(TargetMethod targetMethod)
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.OK);

            // Act
            await ExecuteRequestAsync(targetMethod, Url, HttpMethod.Post, _requestContent);

            // Assert
            Assert.IsNotNull(_actualRequestContent);
            Assert.AreEqual(_requestContent, _actualRequestContent);
        }

        [TestMethod]
        [DataRow(TargetMethod.ResponseContent, DisplayName = "for Action<TContent> callback overload")]
        [DataRow(TargetMethod.NoResponse, DisplayName = "for Action callback overload")]
        [DataRow(TargetMethod.ResponseMessage, DisplayName = "for Action<HttpResponseMessage> callback overload")]
        public async Task SendRequestWithCorrectHeaders(TargetMethod targetMethod)
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.OK);
            var requestMessage = CreateRequestMessage(Url, HttpMethod.Post, headers: _headers);

            // Act
            await ExecuteRequestAsync(targetMethod, requestMessage.RequestUri!.OriginalString, requestMessage.Method, headers: _headers);

            // Assert
            Assert.IsNotNull(_actualRequestHeaders);
            Assert.AreEqual(requestMessage.Headers.ToString(), _actualRequestHeaders.ToString());
        }

        [TestMethod]
        [DataRow(TargetMethod.ResponseContent, DisplayName = "for Action<TContent> callback overload")]
        [DataRow(TargetMethod.NoResponse, DisplayName = "for Action callback overload")]
        [DataRow(TargetMethod.ResponseMessage, DisplayName = "for Action<HttpResponseMessage> callback overload")]
        public async Task InvokeErrorCallbackWithExceptionWhenRequestFails(TargetMethod targetMethod)
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.BadGateway);
            await ExecuteRequestAsync(targetMethod, Url, HttpMethod.Get, _requestContent, errorCallback: ErrorCallback);

            // Act
            _capturedInvokeSynchronized.Invoke();

            // Assert
            _actorDispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsNotNull(_actualException);
            Assert.AreEqual(typeof(HttpRequestException), _actualException.GetType());
        }

        [TestMethod]
        [DataRow(TargetMethod.ResponseContent, DisplayName = "for Action<TContent> callback overload")]
        [DataRow(TargetMethod.NoResponse, DisplayName = "for Action callback overload")]
        [DataRow(TargetMethod.ResponseMessage, DisplayName = "for Action<HttpResponseMessage> callback overload")]
        public async Task InvokeErrorCallbackWithExceptionWhenRequestTimesOut(TargetMethod targetMethod)
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.OK, delay: TimeSpan.FromMilliseconds(500));
            await ExecuteRequestAsync(targetMethod,
                                      Url,
                                      HttpMethod.Get,
                                      _requestContent,
                                      errorCallback: ErrorCallback,
                                      timeout: TimeSpan.FromMilliseconds(10));

            // Act
            _capturedInvokeSynchronized.Invoke();

            // Assert
            _actorDispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsNotNull(_actualException);
            Assert.AreEqual(typeof(TimeoutException), _actualException.GetType());
        }

        [TestMethod]
        [DataRow(TargetMethod.ResponseContent, DisplayName = "for Action<TContent> callback overload")]
        [DataRow(TargetMethod.NoResponse, DisplayName = "for Action callback overload")]
        [DataRow(TargetMethod.ResponseMessage, DisplayName = "for Action<HttpResponseMessage> callback overload")]
        public async Task NotThrowWhenInvokeSynchronizedThrows(TargetMethod targetMethod)
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.Forbidden);
            _actorDispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Throws<Exception>();

            // Act
            await ExecuteRequestAsync(targetMethod, Url, HttpMethod.Get, errorCallback: ErrorCallback);

            // Assert
            _actorDispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
        }

        [TestMethod]
        public async Task InvokeErrorCallbackWithExceptionWhenRetrievingResponseFails()
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.OK);
            await ExecuteRequestAsync(TargetMethod.ResponseContent, Url, HttpMethod.Get, getResponseContent: _ => throw new Exception(), errorCallback: ErrorCallback);

            // Act
            _capturedInvokeSynchronized.Invoke();

            // Assert
            _actorDispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsNotNull(_actualException);
            Assert.AreEqual(typeof(Exception), _actualException.GetType());
        }

        [TestMethod]
        public async Task InvokeSuccessCallbackWithResponseContentOnSuccess()
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.OK, _responseContent);
            await ExecuteRequestAsync(TargetMethod.ResponseContent,
                                      Url,
                                      HttpMethod.Get,
                                      getResponseContent: GetResponseContentAsync,
                                      successCallbackResponseContent: SuccessCallback,
                                      errorCallback: ErrorCallback);

            // Act
            _capturedInvokeSynchronized.Invoke();

            // Assert
            _actorDispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsNotNull(_actualResponseContent);
            Assert.AreEqual(_responseContent, _actualResponseContent);
        }

        [TestMethod]
        public async Task InvokeSuccessCallbackWithoutResponseOnSuccess()
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.OK);
            await ExecuteRequestAsync(TargetMethod.NoResponse, Url, HttpMethod.Get, successCallbackNoContent: SuccessCallback);

            // Act
            _capturedInvokeSynchronized.Invoke();

            // Assert
            _actorDispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsTrue(_successCallbackInvoked);
        }

        [TestMethod]
        public async Task InvokeSuccessCallbackWithHttpResponseOnSuccess()
        {
            // Arrange
            const HttpStatusCode statusCode = HttpStatusCode.OK;
            SetupHttpClientFactory(statusCode, _responseContent);
            await ExecuteRequestAsync(TargetMethod.ResponseMessage, Url, HttpMethod.Get, successCallbackResponseMessage: SuccessCallback);

            // Act
            _capturedInvokeSynchronized.Invoke();

            // Assert
            _actorDispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Once);
            Assert.IsNotNull(_actualResponseMessage);

            var actualResponseContent = await _actualResponseMessage.Content.ReadAsStringAsync(CancellationToken.None);
            Assert.AreEqual(_responseContent, actualResponseContent);
            Assert.AreEqual(statusCode, _actualResponseMessage.StatusCode);
        }

        [TestMethod]
        [DataRow(TargetMethod.NoResponse, DisplayName = "for Action callback overload")]
        [DataRow(TargetMethod.ResponseMessage, DisplayName = "for Action<HttpResponseMessage> callback overload")]
        public async Task NotInvokeDispatcherWhenSuccessCallbackIsNull(TargetMethod targetMethod)
        {
            // Arrange
            SetupHttpClientFactory(HttpStatusCode.OK);

            // Act
            await ExecuteRequestAsync(targetMethod, Url, HttpMethod.Get);

            // Assert
            _actorDispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Never);
        }

        private void SetupCaptureInvokeSynchronized()
        {
            _actorDispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => _capturedInvokeSynchronized = action);
        }

        private void SetupHttpClientFactory(HttpStatusCode statusCode, string? responseContent = null, TimeSpan? delay = null)
        {
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                       .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                       .Callback<HttpRequestMessage, CancellationToken>(async void (request, cancellationToken) =>
                                                                        {
                                                                            try
                                                                            {
                                                                                _actualRequestUri = request.RequestUri;
                                                                                _actualRequestMethod = request.Method;
                                                                                _actualRequestHeaders = request.Headers;

                                                                                if (request.Content != null)
                                                                                {
                                                                                    _actualRequestContent = await request.Content.ReadAsStringAsync(cancellationToken);
                                                                                }
                                                                            }
                                                                            catch (Exception)
                                                                            {
                                                                                // ignore
                                                                            }
                                                                        })
                       .Returns(async (HttpRequestMessage _, CancellationToken cancellationToken) =>
                                {
                                    if (delay.HasValue)
                                    {
                                        await Task.Delay(delay.Value, cancellationToken);
                                    }

                                    return new HttpResponseMessage { StatusCode = statusCode, Content = responseContent != null ? new StringContent(responseContent) : null };
                                });

            var httpClient = new HttpClient(handlerMock.Object);
            _clientFactoryMock.Setup(factory => factory.CreateClient(It.IsAny<string>())).Returns(httpClient);
        }

        private static HttpRequestMessage CreateRequestMessage(string url, HttpMethod httpMethod, string? requestContent = null, Dictionary<string, string>? headers = null)
        {
            var expectedRequestMessage = new HttpRequestMessage(httpMethod, url) { Content = requestContent != null ? new StringContent(requestContent) : null };
            if (headers == null)
            {
                return expectedRequestMessage;
            }

            foreach (var header in headers)
            {
                expectedRequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return expectedRequestMessage;
        }

        private static HttpMethod GetHttpMethod(string httpMethodString)
        {
            return httpMethodString switch
            {
                "GET" => HttpMethod.Get,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                _ => throw new ArgumentOutOfRangeException(nameof(httpMethodString)),
            };
        }

        private static async Task<TestObject> GetResponseContentAsync(HttpResponseMessage response)
        {
            var responseContent = await response.Content.ReadAsStringAsync(CancellationToken.None);
            return JsonSerializer.Deserialize<TestObject>(responseContent)!;
        }

        private void SuccessCallback()
        {
            _successCallbackInvoked = true;
        }

        private void SuccessCallback(TestObject responseContent)
        {
            _actualResponseContent = JsonSerializer.Serialize(responseContent);
        }

        private void SuccessCallback(HttpResponseMessage responseMessage)
        {
            _actualResponseMessage = responseMessage;
        }

        private void ErrorCallback(Exception exception)
        {
            _actualException = exception;
        }

        private async Task ExecuteRequestAsync(TargetMethod targetMethod,
                                               string url,
                                               HttpMethod httpMethod,
                                               string? requestContent = null,
                                               Func<HttpResponseMessage, Task<TestObject>>? getResponseContent = null,
                                               Action<TestObject>? successCallbackResponseContent = null,
                                               Action? successCallbackNoContent = null,
                                               Action<HttpResponseMessage>? successCallbackResponseMessage = null,
                                               Action<Exception>? errorCallback = null,
                                               Dictionary<string, string>? headers = null,
                                               TimeSpan? timeout = null)
        {
            var requestHttpContent = requestContent != null ? new StringContent(requestContent) : null;
            switch (targetMethod)
            {
                case TargetMethod.ResponseContent:
                    await _sut.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                   url,
                                                   httpMethod,
                                                   getResponseContent!,
                                                   successCallbackResponseContent!,
                                                   errorCallback,
                                                   headers,
                                                   requestHttpContent,
                                                   timeout);
                    break;
                case TargetMethod.NoResponse:
                    await _sut.ExecuteRequestAsync(_actorDispatcherMock.Object,
                                                   url,
                                                   httpMethod,
                                                   successCallbackNoContent,
                                                   errorCallback,
                                                   headers,
                                                   requestHttpContent,
                                                   timeout);
                    break;
                case TargetMethod.ResponseMessage:
                    var request = CreateRequestMessage(url, httpMethod, requestContent, headers);
                    await _sut.ExecuteRequestAsync(_actorDispatcherMock.Object, request, successCallbackResponseMessage!, errorCallback, timeout);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(httpMethod), httpMethod, null);
            }
        }
    }
}