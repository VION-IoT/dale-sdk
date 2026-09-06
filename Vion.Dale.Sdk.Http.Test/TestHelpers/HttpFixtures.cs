using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Http.Test.TestHelpers
{
    /// <summary>An <see cref="HttpResponseMessage" /> that counts how often it was disposed.</summary>
    internal sealed class CountingHttpResponse : HttpResponseMessage
    {
        public int Disposals { get; private set; }

        public CountingHttpResponse(HttpStatusCode statusCode, string jsonBody) : base(statusCode)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        protected override void Dispose(bool disposing)
        {
            Disposals++;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///     Content whose bytes arrive only when the test releases them, which is what
    ///     <see cref="HttpCompletionOption.ResponseHeadersRead" /> looks like from the package's side: the
    ///     headers are in and the body is not. Every claim about what happens before the body arrives is
    ///     written against this.
    /// </summary>
    internal sealed class GatedHttpContent : HttpContent
    {
        private readonly byte[] _body;

        private readonly TaskCompletionSource<bool> _released = new();

        public GatedHttpContent(string jsonBody)
        {
            _body = Encoding.UTF8.GetBytes(jsonBody);
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        /// <summary>Lets the body through.</summary>
        public void Release()
        {
            _released.TrySetResult(true);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await _released.Task.ConfigureAwait(false);
            await stream.WriteAsync(_body, 0, _body.Length).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;

            return false;
        }
    }

    /// <summary>Hands out one client, so a test can dispose the very client the package will use.</summary>
    internal sealed class SingleHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingleHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    /// <summary>
    ///     Composes the package the way a consumer does — through the real
    ///     <see cref="ServiceCollectionExtensions.AddDaleHttpSdk" />
    ///     and the real named client — with only the innermost handler replaced. Everything the registration
    ///     configures (the timeout, the User-Agent, the three lifetimes, the handler chain) is therefore under
    ///     test rather than reconstructed by the test.
    /// </summary>
    internal static class HttpSdk
    {
        internal static ServiceProvider Compose(StubHttpMessageHandler handler, Action<HttpClient>? configureClient = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDaleHttpSdk(configureClient);
            services.AddHttpClient(HttpRequestExecutor.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);

            return services.BuildServiceProvider();
        }

        internal static ILogicBlockHttpClient ComposeClient(StubHttpMessageHandler handler, Action<HttpClient>? configureClient = null)
        {
            return Compose(handler, configureClient).GetRequiredService<ILogicBlockHttpClient>();
        }
    }

    /// <summary>
    ///     A converter a consumer could plausibly register that refuses the type it is asked for. It stands
    ///     for the family of bodies the configured serializer cannot write — a cycle, a property getter that
    ///     throws, a converter of the consumer's own — all of which surface where the serialization runs.
    /// </summary>
    internal sealed class RefusingJsonConverter : JsonConverter<TestObject>
    {
        public override TestObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new JsonException("this converter refuses to read");
        }

        public override void Write(Utf8JsonWriter writer, TestObject value, JsonSerializerOptions options)
        {
            throw new JsonException("this converter refuses to write");
        }
    }

    /// <summary>The response shape every deserialization claim in this project is written against.</summary>
    internal sealed class TestObject
    {
        internal const string PascalCaseJson = "{\"StringValue\":\"pinned\",\"IntValue\":42}";

        public string StringValue { get; set; } = null!;

        public int IntValue { get; set; }
    }
}