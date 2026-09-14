using System;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Http.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.Http.LogicBlocks;

namespace Vion.Examples.Http.Test
{
    /// <summary>
    ///     Builds an <see cref="HttpDebugClient" /> whose clock, request timeouts and test context are one virtual clock, so a
    ///     test decides both how long a request took and when its per-request timeout elapses.
    /// </summary>
    internal sealed class DebugClientFixture : IDisposable
    {
        public FakeTimeProvider Clock { get; } = new();

        /// <summary>The requests the block issued and the answers the test scripts.</summary>
        public FakeHttpHarness Harness { get; }

        public HttpDebugClient Sut { get; }

        public DebugClientFixture()
        {
            Harness = new FakeHttpHarness(Clock);
            Sut = new HttpDebugClient(Harness.Client, Clock, LogicBlockTestHelper.CreateLoggerMock().Object);
        }

        public void Dispose()
        {
            Harness.Dispose();
        }

        public LogicBlockTestContext<HttpDebugClient> Build()
        {
            return Sut.CreateTestContext().WithTimeProvider(Clock).Build();
        }
    }
}