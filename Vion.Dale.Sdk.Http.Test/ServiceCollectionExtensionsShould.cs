using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Http.Test.TestHelpers;

namespace Vion.Dale.Sdk.Http.Test
{
    /// <summary>
    ///     What one <c>AddDaleHttpSdk()</c> buys a consumer: three services, one named client, two defaults
    ///     and whatever the caller's own configuration then changes. Everything here composes the real
    ///     registration and reads the result off the container or off the outgoing request, never off a
    ///     reconstruction of what the registration is believed to do.
    ///     <para>
    ///         Requests are made through the executor resolved from that same container and are awaited, so
    ///         every assertion runs after the exchange has settled. The client's own eight members are
    ///         <c>void</c> by design and are proven in <c>LogicBlockHttpClientShould</c>.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ServiceCollectionExtensionsShould
    {
        private const string Url = "http://vion.test/resource";

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-001.1")]
        [DataRow(typeof(ILogicBlockHttpClient))]
        [DataRow(typeof(IHttpRequestExecutor))]
        [DataRow(typeof(IHttpContentSerializer))]
        public void RegisterEveryServiceAsTransient(Type serviceType)
        {
            // Arrange
            var serviceCollection = new ServiceCollection();

            // Act
            serviceCollection.AddDaleHttpSdk();

            // Assert
            var descriptor = serviceCollection.SingleOrDefault(service => service.ServiceType == serviceType);
            Assert.IsNotNull(descriptor, $"{serviceType.Name} was not registered.");
            Assert.AreEqual(ServiceLifetime.Transient, descriptor.Lifetime);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-001.1")]
        public async Task ResolveClientThroughFactoryUnderOneName()
        {
            // Arrange — the stub sits behind the package's own named client, so a request only arrives if
            // the executor resolved that name and nothing else
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var executor = HttpSdk.Compose(handler).GetRequiredService<IHttpRequestExecutor>();
            var dispatcher = new RecordingDispatcher();

            // Act
            await executor.ExecuteRequestAsync(dispatcher, Url, HttpMethod.Get, () => { });

            // Assert — the stub was registered against the package's own client name and nothing else, so
            // a request arriving at all is the executor having resolved that one name
            Assert.HasCount(1, handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-001.2")]
        public void ConfigureThirtySecondTimeoutAndVionUserAgent()
        {
            // Arrange
            var serviceProvider = HttpSdk.Compose(StubHttpMessageHandler.Answering(HttpStatusCode.OK));

            // Act
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpRequestExecutor.HttpClientName);

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(30), httpClient.Timeout);
            Assert.AreEqual("Vion-DALE (info@vion-iot.com)", httpClient.DefaultRequestHeaders.UserAgent.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-001.2")]
        [DataRow(true, DisplayName = "caller replaces the timeout")]
        [DataRow(false, DisplayName = "caller replaces the User-Agent")]
        public void RunCallerConfigurationAfterDefaults(bool replacesTimeout)
        {
            // Arrange — whichever default the caller leaves alone must still be there, or the package would
            // be applying its defaults last and silently discarding the caller's
            var serviceProvider = HttpSdk.Compose(StubHttpMessageHandler.Answering(HttpStatusCode.OK),
                                                  configuredClient =>
                                                  {
                                                      if (replacesTimeout)
                                                      {
                                                          configuredClient.Timeout = TimeSpan.FromSeconds(3);
                                                      }
                                                      else
                                                      {
                                                          configuredClient.DefaultRequestHeaders.Remove("User-Agent");
                                                          configuredClient.DefaultRequestHeaders.Add("User-Agent", "Consumer/1.0");
                                                      }
                                                  });

            // Act
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpRequestExecutor.HttpClientName);

            // Assert
            Assert.AreEqual(replacesTimeout ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(30), httpClient.Timeout);
            Assert.AreEqual(replacesTimeout ? "Vion-DALE (info@vion-iot.com)" : "Consumer/1.0", httpClient.DefaultRequestHeaders.UserAgent.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-001.3")]
        [DataRow(1, DisplayName = "registered once")]
        [DataRow(2, DisplayName = "registered twice")]
        [DataRow(3, DisplayName = "registered three times")]
        public async Task SendOneUserAgentHoweverOftenRegistered(int registrations)
        {
            // Arrange — read off the outgoing request rather than off DefaultRequestHeaders, because what a
            // server rejects is the header on the wire. A composed plugin whose two libraries each register
            // the SDK is the shape that produces more than one.
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var services = new ServiceCollection();
            services.AddLogging();
            for (var registration = 0; registration < registrations; registration++)
            {
                services.AddDaleHttpSdk();
            }

            services.AddHttpClient(HttpRequestExecutor.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
            var executor = services.BuildServiceProvider().GetRequiredService<IHttpRequestExecutor>();

            // Act
            await executor.ExecuteRequestAsync(new RecordingDispatcher(), Url, HttpMethod.Get, () => { });

            // Assert
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual("Vion-DALE (info@vion-iot.com)", handler.LastRequest.Headers.UserAgent.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-001.4")]
        public async Task DeliverCallerConfigurationFailureToErrorCallback()
        {
            // Arrange — a mistake made once at registration is met once per request, forever, and nothing
            // in what the block receives names registration as the cause
            var executor = HttpSdk.Compose(StubHttpMessageHandler.Answering(HttpStatusCode.OK), _ => throw new InvalidOperationException("configureClient failed"))
                                  .GetRequiredService<IHttpRequestExecutor>();
            var dispatcher = new RecordingDispatcher();
            Exception? received = null;

            // Act
            await executor.ExecuteRequestAsync(dispatcher, Url, HttpMethod.Get, () => { }, exception => received = exception);
            dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<InvalidOperationException>(received);
            Assert.AreEqual("configureClient failed", received.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-002.1")]
        public void AddNoPrimaryHandlerOfItsOwn()
        {
            // Arrange — the registration under test, with nothing of the test's own in the chain
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDaleHttpSdk();
            var handlerFactory = services.BuildServiceProvider().GetRequiredService<IHttpMessageHandlerFactory>();

            // Act
            var chain = new List<HttpMessageHandler>();
            for (var handler = handlerFactory.CreateHandler(HttpRequestExecutor.HttpClientName); handler != null;)
            {
                chain.Add(handler);
                handler = handler is DelegatingHandler delegating ? delegating.InnerHandler : null;
            }

            // Assert — the innermost handler is the platform's own, so redirect following, decompression
            // and the connection pool are its defaults rather than anything this package promises
            Assert.IsInstanceOfType<SocketsHttpHandler>(chain[chain.Count - 1]);
        }
    }
}