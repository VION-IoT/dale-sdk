using System;
using System.Linq;
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

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.13")]
        public void AddHeaderToCopyLeavingOriginalUnchanged()
        {
            // Arrange — built the way a simulator builds its throttled answer
            var original = HttpServerResponse.Json("{\"title\":\"Too Many Requests\"}", HttpStatusCode.TooManyRequests);

            // Act
            var response = original.WithHeader("Retry-After", "120").WithHeader("X-Probe", "a").WithHeader("x-probe", "b");

            // Assert
            Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.AreEqual("application/json", response.ContentType);
            CollectionAssert.AreEqual(original.Body.ToArray(), response.Body.ToArray());
            Assert.AreEqual("Retry-After: 120|X-Probe: a|x-probe: b", string.Join("|", response.Headers.Select(header => header.Key + ": " + header.Value)));
            Assert.IsEmpty(original.Headers);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.14")]
        [DataRow("Retry-After\r\nSet-Cookie", DisplayName = "a carriage return and line feed")]
        [DataRow("Retry-After\n", DisplayName = "a bare line feed")]
        [DataRow("Retry After", DisplayName = "a space")]
        [DataRow("Retry-After:", DisplayName = "a colon")]
        [DataRow("Retry-After\t", DisplayName = "a tab")]
        [DataRow("Retry-Äfter", DisplayName = "a character outside ASCII")]
        [DataRow("", DisplayName = "an empty name")]
        [DataRow(null, DisplayName = "no name")]
        [DataRow("Content-Type", DisplayName = "the content type")]
        [DataRow("content-length", DisplayName = "the content length, in lower case")]
        [DataRow("CONNECTION", DisplayName = "the connection, in upper case")]
        [DataRow("Transfer-Encoding", DisplayName = "a transfer encoding")]
        public void RefuseHeaderNameServerCannotWrite(string? name)
        {
            // Arrange
            var response = HttpServerResponse.Json("{}", HttpStatusCode.TooManyRequests);

            // Act / Assert
            Assert.AreEqual("name", Assert.ThrowsExactly<ArgumentException>(() => response.WithHeader(name!, "120")).ParamName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.14")]
        [DataRow("120\r\nSet-Cookie: session=forged", DisplayName = "a carriage return and line feed")]
        [DataRow("120\nSet-Cookie: session=forged", DisplayName = "a bare line feed")]
        [DataRow("120\rX: y", DisplayName = "a bare carriage return")]
        [DataRow("120\0", DisplayName = "another control character")]
        [DataRow("Grüezi", DisplayName = "a character outside ASCII")]
        [DataRow(null, DisplayName = "no value")]
        public void RefuseHeaderValueHeaderBlockCannotCarry(string? value)
        {
            // Arrange
            var response = HttpServerResponse.Json("{}", HttpStatusCode.TooManyRequests);

            // Act / Assert
            Assert.AreEqual("value", Assert.ThrowsExactly<ArgumentException>(() => response.WithHeader("Retry-After", value!)).ParamName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.14")]
        [DataRow("X-Note", "a\tb", DisplayName = "a tab in the value")]
        [DataRow("Retry-After", "", DisplayName = "an empty value")]
        public void AcceptHeaderHeaderBlockCanCarry(string name, string value)
        {
            // Arrange
            var response = HttpServerResponse.Json("{}");

            // Act
            var withHeader = response.WithHeader(name, value);

            // Assert
            Assert.AreEqual(value, withHeader.Headers.Single().Value);
        }
    }
}