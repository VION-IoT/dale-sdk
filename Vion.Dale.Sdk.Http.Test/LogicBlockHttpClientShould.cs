using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Http.Test.TestHelpers;

namespace Vion.Dale.Sdk.Http.Test
{
    /// <summary>
    ///     The eight members a block author calls, against a mocked executor: what each one hands over, which
    ///     method it sends, and whether it asks for the body back. The executor is the seam here because these
    ///     members do exactly one thing — map a member to an executor call — which makes them synchronous and
    ///     therefore free of any waiting. What the executor then does is <c>HttpRequestExecutorShould</c>'s.
    ///     <para>
    ///         Every family below captures the same record off whichever executor overload the member reached,
    ///         so the rows vary only in which member is called and never in the shape of the arrangement.
    ///     </para>
    /// </summary>
    [TestClass]
    public class LogicBlockHttpClientShould
    {
        private const string Url = "http://vion.test/resource";

        /// <summary>
        ///     The bound on a settlement this suite expects to be immediate. The one member proven through
        ///     the real stack is <c>void</c>, so the handler's own signal is the only handle on the exchange;
        ///     it is not a race, because the handler answers as soon as the request reaches it.
        /// </summary>
        private static readonly TimeSpan SettlementTimeout = TimeSpan.FromSeconds(10);

        /// <summary>The eight members of the client, as the rows of the families above.</summary>
        public enum Member
        {
            GetJson,

            PostJson,

            PostJsonWithoutResponse,

            PutJson,

            PutJsonWithoutResponse,

            DeleteJson,

            Delete,

            SendRequest,
        }

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly Action<Exception> _errorCallback = _ => { };

        private readonly Dictionary<string, string> _headers = new() { { "Authorization", "Bearer token" } };

        private readonly Mock<ILogger<LogicBlockHttpClient>> _loggerMock = new();

        private readonly TestObject _requestBody = new() { StringValue = "pinned", IntValue = 42 };

        private readonly Mock<IHttpRequestExecutor> _requestExecutorMock = new();

        private readonly StringContent _serializedBody = new("serialized");

        private readonly Mock<IHttpContentSerializer> _serializerMock = new();

        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

        private ExecutorCall? _executorCall;

        private LogicBlockHttpClient _sut = null!;

        [TestInitialize]
        public void TestInitialize()
        {
            _serializerMock.Setup(serializer => serializer.SerializeJson(_requestBody)).Returns(_serializedBody);
            CaptureExecutorCall();
            _sut = new LogicBlockHttpClient(_requestExecutorMock.Object, _serializerMock.Object, _loggerMock.Object);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-003.1")]
        public void ReturnBeforeExchangeCompletes()
        {
            // Arrange — an executor that never finishes; a member that awaited it would never return
            var pending = new TaskCompletionSource<bool>();
            _requestExecutorMock.Setup(executor => executor.ExecuteRequestAsync(It.IsAny<IActorDispatcher>(),
                                                                                It.IsAny<string>(),
                                                                                It.IsAny<HttpMethod>(),
                                                                                It.IsAny<Action>(),
                                                                                It.IsAny<Action<Exception>>(),
                                                                                It.IsAny<Dictionary<string, string>>(),
                                                                                It.IsAny<HttpContent>(),
                                                                                It.IsAny<TimeSpan?>()))
                                .Returns(pending.Task);

            // Act
            _sut.Delete(_dispatcherMock.Object, Url, () => { }, _errorCallback);

            // Assert — reaching this line at all is the behaviour; the exchange is still outstanding
            Assert.IsFalse(pending.Task.IsCompleted);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-003.1")]
        [DataRow(Member.GetJson)]
        [DataRow(Member.PostJson)]
        [DataRow(Member.PostJsonWithoutResponse)]
        [DataRow(Member.PutJson)]
        [DataRow(Member.PutJsonWithoutResponse)]
        [DataRow(Member.DeleteJson)]
        [DataRow(Member.Delete)]
        [DataRow(Member.SendRequest)]
        public void PassDispatcherAndTimeoutOfCallerToExecutor(Member member)
        {
            // Arrange

            // Act
            Invoke(member);

            // Assert — the block the author passed is the one the callbacks will run on
            Assert.IsNotNull(_executorCall);
            Assert.AreSame(_dispatcherMock.Object, _executorCall.Dispatcher);
            Assert.AreEqual(_timeout, _executorCall.Timeout);
            Assert.AreSame(_errorCallback, _executorCall.ErrorCallback);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-003.1")]
        [DataRow(Member.GetJson)]
        [DataRow(Member.PostJson)]
        [DataRow(Member.PostJsonWithoutResponse)]
        [DataRow(Member.PutJson)]
        [DataRow(Member.PutJsonWithoutResponse)]
        [DataRow(Member.DeleteJson)]
        [DataRow(Member.Delete)]
        public void PassUrlAndHeadersOfCallerToExecutor(Member member)
        {
            // Arrange

            // Act
            Invoke(member);

            // Assert — SendRequest is absent by design: it carries both on the message it was handed
            Assert.IsNotNull(_executorCall);
            Assert.AreEqual(Url, _executorCall.Url);
            Assert.AreSame(_headers, _executorCall.Headers);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-003.2")]
        [DataRow(Member.GetJson, "GET")]
        [DataRow(Member.PostJson, "POST")]
        [DataRow(Member.PostJsonWithoutResponse, "POST")]
        [DataRow(Member.PutJson, "PUT")]
        [DataRow(Member.PutJsonWithoutResponse, "PUT")]
        [DataRow(Member.DeleteJson, "DELETE")]
        [DataRow(Member.Delete, "DELETE")]
        public void SendMethodNamingEachMember(Member member, string expectedMethod)
        {
            // Arrange

            // Act
            Invoke(member);

            // Assert
            Assert.IsNotNull(_executorCall?.HttpMethod);
            Assert.AreEqual(expectedMethod, _executorCall.HttpMethod.Method);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-003.2")]
        [DataRow(Member.GetJson, true)]
        [DataRow(Member.PostJson, true)]
        [DataRow(Member.PutJson, true)]
        [DataRow(Member.DeleteJson, true)]
        [DataRow(Member.PostJsonWithoutResponse, false)]
        [DataRow(Member.PutJsonWithoutResponse, false)]
        [DataRow(Member.Delete, false)]
        public void AskForResponseBodyOnlyForMembersCarryingResponseType(Member member, bool carriesResponseType)
        {
            // Arrange

            // Act
            Invoke(member);

            // Assert
            Assert.IsNotNull(_executorCall);
            Assert.AreEqual(carriesResponseType, _executorCall.Deserializer != null);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-003.2")]
        [DataRow(Member.GetJson)]
        [DataRow(Member.PostJson)]
        [DataRow(Member.PutJson)]
        [DataRow(Member.DeleteJson)]
        public async Task ReadResponseBodyThroughConfiguredSerializer(Member member)
        {
            // Arrange
            Invoke(member);
            var response = new HttpResponseMessage();

            // Act
            await _executorCall!.Deserializer!(response);

            // Assert
            _serializerMock.Verify(serializer => serializer.DeserializeJsonAsync<TestObject>(response.Content), Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-011.2")]
        [DataRow(Member.PostJson)]
        [DataRow(Member.PostJsonWithoutResponse)]
        [DataRow(Member.PutJson)]
        [DataRow(Member.PutJsonWithoutResponse)]
        public void SerializeRequestBodyBeforeSending(Member member)
        {
            // Arrange

            // Act
            Invoke(member);

            // Assert
            Assert.AreSame(_serializedBody, _executorCall?.RequestContent);
            _serializerMock.Verify(serializer => serializer.SerializeJson(_requestBody), Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-011.2")]
        [DataRow(Member.GetJson)]
        [DataRow(Member.DeleteJson)]
        [DataRow(Member.Delete)]
        public void SendNoBodyForMembersTakingNone(Member member)
        {
            // Arrange

            // Act
            Invoke(member);

            // Assert
            Assert.IsNotNull(_executorCall);
            Assert.IsNull(_executorCall.RequestContent);
            _serializerMock.Verify(serializer => serializer.SerializeJson(It.IsAny<TestObject>()), Times.Never);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-004.1")]
        public void SendRequestGivenWithoutUrlOrHeadersOfItsOwn()
        {
            // Arrange — the caller owns method, URI, headers and content on the message it hands over
            var request = new HttpRequestMessage(HttpMethod.Patch, Url);
            request.Headers.Add("X-Caller", "yes");
            Action<HttpResponseMessage> successCallback = _ => { };

            // Act
            _sut.SendRequest(_dispatcherMock.Object, request, successCallback, _errorCallback, _timeout);

            // Assert
            _requestExecutorMock.Verify(executor => executor.ExecuteRequestAsync(_dispatcherMock.Object, request, successCallback, _errorCallback, _timeout), Times.Once);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-007.3")]
        [DataRow(false, DisplayName = "no request at all")]
        [DataRow(true, DisplayName = "a request with no URI")]
        public void RefuseSendRequestWithoutRequestOrUri(bool hasRequest)
        {
            // Arrange — the real executor, because the refusal has to reach the caller before this member
            // reads the request itself to build its logging continuation; a mocked executor would let that
            // read happen and the caller would get a bare null-reference exception instead
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = new LogicBlockHttpClient(HttpSdk.Compose(handler).GetRequiredService<IHttpRequestExecutor>(), _serializerMock.Object, _loggerMock.Object);
            var request = hasRequest ? new HttpRequestMessage { Method = HttpMethod.Get, RequestUri = null } : null;

            // Act / Assert
            var refusal = Assert.Throws<ArgumentException>(() => sut.SendRequest(_dispatcherMock.Object, request!, _ => { }, _errorCallback));
            Assert.AreEqual("request", refusal.ParamName);
            Assert.Contains("SendRequest", refusal.Message);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-011.3")]
        public async Task SendNullBodyAsJsonNull()
        {
            // Arrange — the `notnull` constraint on the member is the compiler's and not the runtime's, so a
            // call site with nullable reference types off reaches the wire. The whole stack is real, because
            // the criterion is about what leaves rather than about what the serializer returned; the
            // handler's own signal is what makes the assertion wait for the exchange this `void` member
            // gave nobody a handle on.
            var sent = new TaskCompletionSource<string>();
            var handler = StubHttpMessageHandler.Responding(async (request, _) =>
                                                            {
                                                                sent.TrySetResult(request.Content == null ? "<no content>" : await request.Content.ReadAsStringAsync());

                                                                return StubHttpMessageHandler.Respond(HttpStatusCode.OK, TestObject.PascalCaseJson);
                                                            });
            var sut = HttpSdk.ComposeClient(handler);

            // Act
            sut.PostJson<TestObject>(_dispatcherMock.Object, Url, null!);

            // Assert
            Assert.AreEqual("null", await sent.Task.WaitAsync(SettlementTimeout));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-007.4")]
        [DataRow(Member.PostJson)]
        [DataRow(Member.PostJsonWithoutResponse)]
        [DataRow(Member.PutJson)]
        [DataRow(Member.PutJsonWithoutResponse)]
        public void ThrowSerializerRefusalAtCallerWithoutSendingAnything(Member member)
        {
            // Arrange — the real stack, because the serialization runs on the caller's own thread before the
            // executor's task exists; a mocked serializer would prove only that this test can throw. The
            // converter is registered the way `AC-HTTP-011.1` lets a consumer register one.
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDaleHttpSdk();
            services.AddHttpClient(HttpRequestExecutor.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
            services.Configure<JsonSerializerOptions>(options => options.Converters.Add(new RefusingJsonConverter()));
            var sut = services.BuildServiceProvider().GetRequiredService<ILogicBlockHttpClient>();

            // Act / Assert — at the caller, with nothing sent and nothing scheduled onto the block's actor
            Assert.ThrowsExactly<JsonException>(() => Invoke(sut, member));
            Assert.IsEmpty(handler.Requests);
            _dispatcherMock.Verify(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>()), Times.Never);
        }

        /// <summary>
        ///     Records the one executor call a member makes, whichever of the three overloads it reached, so
        ///     the families above vary in the member they call and in nothing else.
        /// </summary>
        private void CaptureExecutorCall()
        {
            _requestExecutorMock.Setup(executor => executor.ExecuteRequestAsync(It.IsAny<IActorDispatcher>(),
                                                                                It.IsAny<string>(),
                                                                                It.IsAny<HttpMethod>(),
                                                                                It.IsAny<Func<HttpResponseMessage, Task<TestObject>>>(),
                                                                                It.IsAny<Action<TestObject>>(),
                                                                                It.IsAny<Action<Exception>>(),
                                                                                It.IsAny<Dictionary<string, string>>(),
                                                                                It.IsAny<HttpContent>(),
                                                                                It.IsAny<TimeSpan?>()))
                                .Callback(new InvocationAction(invocation => _executorCall = new ExecutorCall
                                                                                             {
                                                                                                 Dispatcher = (IActorDispatcher)invocation.Arguments[0],
                                                                                                 Url = (string)invocation.Arguments[1],
                                                                                                 HttpMethod = (HttpMethod)invocation.Arguments[2],
                                                                                                 Deserializer =
                                                                                                     (Func<HttpResponseMessage, Task<TestObject>>)invocation.Arguments[3],
                                                                                                 ErrorCallback = (Action<Exception>?)invocation.Arguments[5],
                                                                                                 Headers = (Dictionary<string, string>?)invocation.Arguments[6],
                                                                                                 RequestContent = (HttpContent?)invocation.Arguments[7],
                                                                                                 Timeout = (TimeSpan?)invocation.Arguments[8],
                                                                                             }))
                                .Returns(Task.CompletedTask);
            _requestExecutorMock.Setup(executor => executor.ExecuteRequestAsync(It.IsAny<IActorDispatcher>(),
                                                                                It.IsAny<string>(),
                                                                                It.IsAny<HttpMethod>(),
                                                                                It.IsAny<Action>(),
                                                                                It.IsAny<Action<Exception>>(),
                                                                                It.IsAny<Dictionary<string, string>>(),
                                                                                It.IsAny<HttpContent>(),
                                                                                It.IsAny<TimeSpan?>()))
                                .Callback(new InvocationAction(invocation => _executorCall = new ExecutorCall
                                                                                             {
                                                                                                 Dispatcher = (IActorDispatcher)invocation.Arguments[0],
                                                                                                 Url = (string)invocation.Arguments[1],
                                                                                                 HttpMethod = (HttpMethod)invocation.Arguments[2],
                                                                                                 ErrorCallback = (Action<Exception>?)invocation.Arguments[4],
                                                                                                 Headers = (Dictionary<string, string>?)invocation.Arguments[5],
                                                                                                 RequestContent = (HttpContent?)invocation.Arguments[6],
                                                                                                 Timeout = (TimeSpan?)invocation.Arguments[7],
                                                                                             }))
                                .Returns(Task.CompletedTask);
            _requestExecutorMock.Setup(executor => executor.ExecuteRequestAsync(It.IsAny<IActorDispatcher>(),
                                                                                It.IsAny<HttpRequestMessage>(),
                                                                                It.IsAny<Action<HttpResponseMessage>>(),
                                                                                It.IsAny<Action<Exception>>(),
                                                                                It.IsAny<TimeSpan?>()))
                                .Callback(new InvocationAction(invocation => _executorCall = new ExecutorCall
                                                                                             {
                                                                                                 Dispatcher = (IActorDispatcher)invocation.Arguments[0],
                                                                                                 ErrorCallback = (Action<Exception>?)invocation.Arguments[3],
                                                                                                 Timeout = (TimeSpan?)invocation.Arguments[4],
                                                                                             }))
                                .Returns(Task.CompletedTask);
        }

        private void Invoke(Member member)
        {
            Invoke(_sut, member);
        }

        private void Invoke(ILogicBlockHttpClient client, Member member)
        {
            switch (member)
            {
                case Member.GetJson:
                    client.GetJson<TestObject>(_dispatcherMock.Object,
                                               Url,
                                               _ => { },
                                               _errorCallback,
                                               _headers,
                                               _timeout);
                    break;
                case Member.PostJson:
                    client.PostJson<TestObject, TestObject>(_dispatcherMock.Object,
                                                            Url,
                                                            _requestBody,
                                                            _ => { },
                                                            _errorCallback,
                                                            _headers,
                                                            _timeout);
                    break;
                case Member.PostJsonWithoutResponse:
                    client.PostJson(_dispatcherMock.Object,
                                    Url,
                                    _requestBody,
                                    () => { },
                                    _errorCallback,
                                    _headers,
                                    _timeout);
                    break;
                case Member.PutJson:
                    client.PutJson<TestObject, TestObject>(_dispatcherMock.Object,
                                                           Url,
                                                           _requestBody,
                                                           _ => { },
                                                           _errorCallback,
                                                           _headers,
                                                           _timeout);
                    break;
                case Member.PutJsonWithoutResponse:
                    client.PutJson(_dispatcherMock.Object,
                                   Url,
                                   _requestBody,
                                   () => { },
                                   _errorCallback,
                                   _headers,
                                   _timeout);
                    break;
                case Member.DeleteJson:
                    client.DeleteJson<TestObject>(_dispatcherMock.Object,
                                                  Url,
                                                  _ => { },
                                                  _errorCallback,
                                                  _headers,
                                                  _timeout);
                    break;
                case Member.Delete:
                    client.Delete(_dispatcherMock.Object,
                                  Url,
                                  () => { },
                                  _errorCallback,
                                  _headers,
                                  _timeout);
                    break;
                case Member.SendRequest:
                    client.SendRequest(_dispatcherMock.Object, new HttpRequestMessage(HttpMethod.Patch, Url), _ => { }, _errorCallback, _timeout);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(member), member, null);
            }
        }

        /// <summary>What one member handed the executor, read back without knowing which overload it used.</summary>
        private sealed class ExecutorCall
        {
            public Func<HttpResponseMessage, Task<TestObject>>? Deserializer { get; init; }

            public IActorDispatcher? Dispatcher { get; init; }

            public Action<Exception>? ErrorCallback { get; init; }

            public Dictionary<string, string>? Headers { get; init; }

            public HttpMethod? HttpMethod { get; init; }

            public HttpContent? RequestContent { get; init; }

            public TimeSpan? Timeout { get; init; }

            public string? Url { get; init; }
        }
    }
}