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
        [DataRow(100, DisplayName = "an interim status, which no final response may carry")]
        [DataRow(199, DisplayName = "the last interim status")]
        [DataRow(600, DisplayName = "past the last status")]
        public void RefuseStatusOutsideFinalResponseRange(int statusCode)
        {
            // Arrange

            // Act / Assert
            Assert.AreEqual("statusCode", Assert.Throws<ArgumentException>(() => new HttpServerResponse((HttpStatusCode)statusCode)).ParamName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.12")]
        [DataRow("application/json\r\nSet-Cookie: session=forged", DisplayName = "a carriage return and line feed")]
        [DataRow("application/json\nSet-Cookie: session=forged", DisplayName = "a bare line feed")]
        [DataRow("application/json\rX: y", DisplayName = "a bare carriage return")]
        [DataRow("text/plain\0", DisplayName = "another control character")]
        [DataRow("text/plain; charset=utf-8; title=Grüezi", DisplayName = "a character outside ASCII")]
        public void RefuseContentTypeHeaderBlockCannotCarry(string contentType)
        {
            // Arrange

            // Act / Assert
            Assert.AreEqual("contentType", Assert.ThrowsExactly<ArgumentException>(() => new HttpServerResponse(HttpStatusCode.OK, contentType)).ParamName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.12")]
        public void AcceptContentTypeWithParametersAndTab()
        {
            // Arrange

            // Act
            var response = new HttpServerResponse(HttpStatusCode.OK, "text/plain;\tcharset=utf-8");

            // Assert
            Assert.AreEqual("text/plain;\tcharset=utf-8", response.ContentType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.11")]
        [DataRow(200)]
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