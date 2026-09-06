using System.Net.Http;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Vion.Dale.Sdk.Http.Test.TestHelpers;

namespace Vion.Dale.Sdk.Http.Test
{
    /// <summary>
    ///     What a block author's types become on the wire and what a server's JSON becomes on the way back —
    ///     including the defaults that make a mismatched body succeed with nothing in it, which is the single
    ///     most consequential thing this package does quietly.
    /// </summary>
    [TestClass]
    public class HttpContentSerializerShould
    {
        private readonly TestObject _testObject = new() { StringValue = "pinned", IntValue = 42 };

        private readonly HttpContentSerializer _sut = new(Options.Create(new JsonSerializerOptions()));

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-011.1")]
        public async Task SerializeWithOptionsConsumerConfigured()
        {
            // Arrange
            var sut = new HttpContentSerializer(Options.Create(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            // Act
            var httpContent = sut.SerializeJson(_testObject);

            // Assert
            Assert.AreEqual("{\"stringValue\":\"pinned\",\"intValue\":42}", await httpContent.ReadAsStringAsync(CancellationToken.None));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-011.1")]
        public async Task DeserializeWithOptionsConsumerConfigured()
        {
            // Arrange — the escape hatch from the casing default below
            var sut = new HttpContentSerializer(Options.Create(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
            var httpContent = new StringContent("{\"stringvalue\":\"pinned\",\"intvalue\":42}");

            // Act
            var result = await sut.DeserializeJsonAsync<TestObject>(httpContent);

            // Assert
            Assert.AreEqual("pinned", result.StringValue);
            Assert.AreEqual(42, result.IntValue);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-011.1")]
        public async Task SerializeWithPlatformDefaultsWhenNothingConfigured()
        {
            // Arrange

            // Act
            var httpContent = _sut.SerializeJson(_testObject);

            // Assert
            Assert.AreEqual(JsonSerializer.Serialize(_testObject), await httpContent.ReadAsStringAsync(CancellationToken.None));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-011.2")]
        public void SendSerializedBodyAsApplicationJsonWithoutCharset()
        {
            // Arrange

            // Act
            var contentType = _sut.SerializeJson(_testObject).Headers.ContentType;

            // Assert — a server that requires a charset parameter will reject this, and only SendRequest
            // lets a caller set its own
            Assert.IsNotNull(contentType);
            Assert.AreEqual(MediaTypeNames.Application.Json, contentType.MediaType);
            Assert.IsNull(contentType.CharSet);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-009.1")]
        public async Task DeserializeMismatchedPropertyNamesToDefaults()
        {
            // Arrange — the body a server with camelCase conventions returns, into a type declared the
            // way C# declares things
            var httpContent = new StringContent("{\"stringvalue\":\"pinned\",\"intvalue\":42}");

            // Act
            var result = await _sut.DeserializeJsonAsync<TestObject>(httpContent);

            // Assert — no error anywhere: the block author gets an object full of nothing
            Assert.IsNull(result.StringValue);
            Assert.AreEqual(0, result.IntValue);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-009.2")]
        [DataRow("{\"StringValue\":\"pinned\",\"IntValue\":42,\"Unexpected\":true}", "pinned", 42, DisplayName = "a property the type does not declare")]
        [DataRow("{\"StringValue\":\"pinned\"}", "pinned", 0, DisplayName = "a property the body omits")]
        public async Task DeserializeSurplusOrMissingPropertiesWithoutError(string json, string expectedStringValue, int expectedIntValue)
        {
            // Arrange
            var httpContent = new StringContent(json);

            // Act
            var result = await _sut.DeserializeJsonAsync<TestObject>(httpContent);

            // Assert
            Assert.AreEqual(expectedStringValue, result.StringValue);
            Assert.AreEqual(expectedIntValue, result.IntValue);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-006.1")]
        public async Task NameFullTypeItCouldNotFillOnNullBody()
        {
            // Arrange — the block author receives this in the error callback, so the type it names has to be
            // the one they declared: two DTOs sharing a short name are indistinguishable otherwise
            var httpContent = new StringContent("null");

            // Act / Assert
            var failure = await Assert.ThrowsAsync<ContentNullAfterDeserializationException>(() => _sut.DeserializeJsonAsync<TestObject>(httpContent));
            Assert.AreEqual($"Content was null after deserialization to type '{typeof(TestObject).FullName}'.", failure.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-011.3")]
        public async Task SerializeNullBodyAsJsonNull()
        {
            // Arrange — the notnull constraint on the member is compile-time only, so a call site with
            // nullable reference types off reaches here with null

            // Act
            var httpContent = _sut.SerializeJson<TestObject>(null!);

            // Assert
            Assert.AreEqual("null", await httpContent.ReadAsStringAsync(CancellationToken.None));
        }
    }
}
