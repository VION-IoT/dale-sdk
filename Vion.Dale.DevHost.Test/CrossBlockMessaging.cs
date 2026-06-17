using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Minimal request/response contract between two blocks — a "Source" polls a "Sink". Mirrors the
    ///     PingPong example's shape; the generator produces the ISource / ISink interfaces, the
    ///     GetLinkedSinks helper, and SendRequest / HandleRequest / HandleResponse.
    /// </summary>
    [LogicBlockContract(BetweenInterface = "ISource", AndInterface = "ISink", Direction = ContractDirection.Bidirectional)]
    public static class PollLink
    {
        [RequestResponse(From = "ISource", To = "ISink", ResponseType = typeof(Ack))]
        public readonly record struct Poll;

        public readonly record struct Ack;
    }

    /// <summary>Sends a single poll to its linked sink on startup — the "orchestrator that polls a device".</summary>
    [LogicBlock(Name = "Source")]
    [LogicBlockInterfaceBinding(typeof(ISource), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class SourceBlock : LogicBlockBase, ISource
    {
        public SourceBlock(ILogger logger) : base(logger)
        {
        }

        public void HandleResponse(InterfaceId functionId, PollLink.Ack response)
        {
            // No-op: this test asserts the request reached the sink, not the response round-trip.
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
            var sinks = this.GetLinkedSinks();
            if (sinks.Count == 1)
            {
                this.SendRequest(sinks.First(), new PollLink.Poll());
            }
        }
    }

    /// <summary>
    ///     A second, independent ISource block — used to construct a catalog-level AutoConnect ambiguity
    ///     (two sources both matching one sink), which the conflict guard must leave unwired.
    /// </summary>
    [LogicBlock(Name = "Source2")]
    [LogicBlockInterfaceBinding(typeof(ISource), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class SecondSourceBlock : LogicBlockBase, ISource
    {
        public SecondSourceBlock(ILogger logger) : base(logger)
        {
        }

        public void HandleResponse(InterfaceId functionId, PollLink.Ack response)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>Receives the poll and records it on an observable property — the "device".</summary>
    [LogicBlock(Name = "Sink")]
    [LogicBlockInterfaceBinding(typeof(ISink), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class SinkBlock : LogicBlockBase, ISink
    {
        [ServiceProperty(Title = "Received polls")]
        public int ReceivedPolls { get; private set; }

        public SinkBlock(ILogger logger) : base(logger)
        {
        }

        public PollLink.Ack HandleRequest(PollLink.Poll request)
        {
            ReceivedPolls++;
            return new PollLink.Ack();
        }

        protected override void Ready()
        {
        }
    }

    public class CrossBlockDependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<SourceBlock>();
            serviceCollection.AddTransient<SinkBlock>();
        }
    }
}