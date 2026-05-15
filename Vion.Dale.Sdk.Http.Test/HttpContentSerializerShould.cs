using System;
using System.Net.Http;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Vion.Dale.Sdk.Http.Test
{
    [TestClass]
    public class HttpContentSerializerShould
    {
        private readonly HttpContentSerializer _sut = new(Options.Create(new JsonSerializerOptions()));

        private readonly TestObject _testObject = new() { StringValue = Guid.NewGuid().ToString(), IntValue = 100 };

        [TestMethod]
        public async Task SerializeObjectToJsonContent()
        {
            // Arrange

            // Act
            var httpContent = _sut.SerializeJson(_testObject);

            // Assert
            var expectedJson = JsonSerializer.Serialize(_testObject);
            var actualJson = await httpContent.ReadAsStringAsync(CancellationToken.None);
            Assert.AreEqual(expectedJson, actualJson);
        }

        [TestMethod]
        public async Task ApplySerializerOptionsWhenSerializing()
        {
            // Arrange
            var sut = new HttpContentSerializer(Options.Create(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            // Act
            var httpContent = sut.SerializeJson(_testObject);

            // Assert
            var expectedJson = $"{{\"stringValue\":\"{_testObject.StringValue}\",\"intValue\":{_testObject.IntValue}}}";
            var actualJson = await httpContent.ReadAsStringAsync(CancellationToken.None);
            Assert.AreEqual(expectedJson, actualJson);
        }

        [TestMethod]
        public void SetContentTypeToApplicationJson()
        {
            // Arrange

            // Act
            var actualMediaType = _sut.SerializeJson(_testObject).Headers.ContentType?.MediaType;

            // Assert
            Assert.AreEqual(MediaTypeNames.Application.Json, actualMediaType);
        }

        [TestMethod]
        public async Task DeserializeJsonContentToObject()
        {
            // Arrange
            var httpContent = new StringContent(JsonSerializer.Serialize(_testObject));

            // Act
            var result = await _sut.DeserializeJsonAsync<TestObject>(httpContent);

            // Assert
            Assert.AreEqual(_testObject.StringValue, result.StringValue);
            Assert.AreEqual(_testObject.IntValue, result.IntValue);
        }

        [TestMethod]
        public async Task ThrowExceptionWhenJsonDeserializesToNull()
        {
            // Arrange
            var httpContent = new StringContent("null");

            // Act & Assert
            await Assert.ThrowsAsync<ContentNullAfterDeserializationException>(() => _sut.DeserializeJsonAsync<TestObject>(httpContent));
        }

        [TestMethod]
        public async Task ApplySerializerOptionsWhenDeserializing()
        {
            // Arrange
            var sut = new HttpContentSerializer(Options.Create(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
            var httpContent = new StringContent($"{{\"stringvalue\": \"{_testObject.StringValue}\", \"intvalue\": {_testObject.IntValue}}}");

            // Act
            var result = await sut.DeserializeJsonAsync<TestObject>(httpContent);

            // Assert
            Assert.AreEqual(_testObject.StringValue, result.StringValue);
            Assert.AreEqual(_testObject.IntValue, result.IntValue);
        }
    }
}