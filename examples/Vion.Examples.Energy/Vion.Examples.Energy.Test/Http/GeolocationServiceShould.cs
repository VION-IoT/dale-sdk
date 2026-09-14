using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Http.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.Energy.Services;
using Xunit;

namespace Vion.Examples.Energy.Test.Http
{
    /// <summary>
    ///     The city lookup against Nominatim, with the answers scripted through the HTTP test kit: the request the service
    ///     builds is read off what the SDK composed for the wire, and every answer reaches the service through the SDK's own
    ///     deserialization and error mapping and then the block's actor.
    /// </summary>
    public sealed class GeolocationServiceShould : IDisposable
    {
        private readonly ServiceHostBlock _block = new(LogicBlockTestHelper.CreateLoggerMock().Object);

        private readonly LogicBlockTestContext<ServiceHostBlock> _context;

        private readonly FakeHttpHarness _harness = new();

        private readonly GeolocationService _sut;

        public GeolocationServiceShould()
        {
            _context = _block.CreateTestContext().Build();
            _sut = new GeolocationService(_harness.Client, NullLogger<GeolocationService>.Instance);
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        [Fact]
        public void RequestCityFromNominatimWithEncodedName()
        {
            // Arrange

            // Act
            _sut.GetCoordinates(_block, "Zürich", _ => { });

            // Assert
            Assert.Equal("https://nominatim.openstreetmap.org/search?format=json&q=Z%c3%bcrich", Assert.Single(_harness.Requests).Uri.OriginalString);
        }

        [Fact]
        public void DeliverCoordinatesOfFirstResult()
        {
            // Arrange
            (double Latitude, double Longitude)? coordinates = null;
            _sut.GetCoordinates(_block, "Winterthur", received => coordinates = received);

            // Act
            _harness.Respond("[{\"lat\":\"47.4991723\",\"lon\":\"8.7291498\",\"display_name\":\"Winterthur\"},{\"lat\":\"1\",\"lon\":\"2\"}]");
            _context.FlushPendingActions();

            // Assert
            Assert.Equal((47.4991723, 8.7291498), coordinates);
        }

        [Fact]
        public void ReportNoResultsForEmptyAnswer()
        {
            // Arrange
            Exception? failure = null;
            _sut.GetCoordinates(_block, "Atlantis", _ => { }, error => failure = error);

            // Act
            _harness.Respond("[]");
            _context.FlushPendingActions();

            // Assert
            Assert.Equal("No geolocation results found", Assert.IsType<ArgumentException>(failure).Message);
        }

        [Fact]
        public void PassNonSuccessStatusToErrorCallback()
        {
            // Arrange
            Exception? failure = null;
            _sut.GetCoordinates(_block, "Winterthur", _ => { }, error => failure = error);

            // Act
            _harness.Respond(HttpStatusCode.TooManyRequests);
            _context.FlushPendingActions();

            // Assert
            Assert.Equal("Response status code does not indicate success: 429 (Too Many Requests).", Assert.IsType<HttpRequestException>(failure).Message);
        }

        [Fact]
        public void PassTransportFailureToErrorCallback()
        {
            // Arrange
            Exception? failure = null;
            _sut.GetCoordinates(_block, "Winterthur", _ => { }, error => failure = error);
            var unreachable = new SocketException((int)SocketError.HostNotFound);

            // Act
            _harness.Fail(unreachable);
            _context.FlushPendingActions();

            // Assert
            Assert.Same(unreachable, failure);
        }
    }
}
