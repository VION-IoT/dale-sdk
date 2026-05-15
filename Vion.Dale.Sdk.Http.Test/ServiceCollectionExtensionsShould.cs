using System;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Http.Test
{
    [TestClass]
    public class ServiceCollectionExtensionsShould
    {
        [TestMethod]
        public void RegisterHttpClientRelatedServices()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();

            // Act
            serviceCollection.AddDaleHttpSdk();

            // Assert
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IHttpRequestExecutor) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IHttpContentSerializer) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(ILogicBlockHttpClient) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IHttpClientFactory)));
        }

        [TestMethod]
        public void ConfigureHttpClientWithDefaults()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddDaleHttpSdk();
            var serviceProvider = serviceCollection.BuildServiceProvider();
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // Act
            var httpClient = httpClientFactory.CreateClient(HttpRequestExecutor.HttpClientName);

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(30), httpClient.Timeout);
            Assert.Contains("Vion-DALE", httpClient.DefaultRequestHeaders.UserAgent.ToString());
        }

        [TestMethod]
        public void InvokeConfigureClientAction()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            var actionInvoked = false;
            serviceCollection.AddDaleHttpSdk(_ => actionInvoked = true);
            var serviceProvider = serviceCollection.BuildServiceProvider();
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

            // Act
            httpClientFactory.CreateClient(HttpRequestExecutor.HttpClientName);

            // Assert
            Assert.IsTrue(actionInvoked);
        }
    }
}