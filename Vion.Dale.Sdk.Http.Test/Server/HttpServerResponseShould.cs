using System;
using System.Net;
using Vion.Dale.Sdk.Http.Server;

namespace Vion.Dale.Sdk.Http.Test.Server
{
    [TestClass]
    public class HttpServerResponseShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.11")]
        [DataRow(99)]
        [DataRow(600)]
        public void RefuseStatusOutsideHttpRange(int statusCode)
        {
            // Arrange

            // Act / Assert
            Assert.AreEqual("statusCode", Assert.Throws<ArgumentException>(() => new HttpServerResponse((HttpStatusCode)statusCode)).ParamName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.11")]
        [DataRow(100)]
        [DataRow(599)]
        public void AcceptStatusAtEitherEdgeOfHttpRange(int statusCode)
        {
            // Arrange

            // Act
            var response = new HttpServerResponse((HttpStatusCode)statusCode);

            // Assert
            Assert.AreEqual(statusCode, (int)response.StatusCode);
        }
    }
}