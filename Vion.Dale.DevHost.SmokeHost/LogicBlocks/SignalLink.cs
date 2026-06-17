using System.Linq;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.SmokeHost.LogicBlocks
{
    /// <summary>
    ///     A minimal request/response contract between two blocks — exercises inter-block wiring: the
    ///     generated <c>ISignalSource</c> / <c>ISignalSink</c> interfaces, interface mappings in the
    ///     topology (a real link in the topology view), <c>SendRequest</c> / <c>HandleRequest</c> /
    ///     <c>HandleResponse</c>, and the message tap. Mirrors the PingPong shape.
    /// </summary>
    [LogicBlockContract(BetweenInterface = "ISignalSource", AndInterface = "ISignalSink", Direction = ContractDirection.Bidirectional)]
    public static class SignalLink
    {
        [RequestResponse(From = "ISignalSource", To = "ISignalSink", ResponseType = typeof(Ack))]
        public readonly record struct Ping(int Sequence);

        public readonly record struct Ack(int Sequence);
    }

    /// <summary>Pings its linked sink once per virtual second — so the link is live and a scenario can advance + observe it.</summary>
    [LogicBlock(Name = "Signal Source", Icon = "broadcast-line")]
    [LogicBlockInterfaceBinding(typeof(ISignalSource), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class SignalSourceBlock : LogicBlockBase, ISignalSource
    {
        private int _sequence;

        [ServiceProperty(Title = "Gesendete Pings")]
        [Presentation(Group = PropertyGroup.Metric, Importance = Importance.Primary)]
        public int SentPings { get; private set; }

        [ServiceProperty(Title = "Letzte bestätigte Sequenz")]
        [Presentation(Group = PropertyGroup.Status)]
        public int LastAck { get; private set; }

        public SignalSourceBlock(ILogger logger) : base(logger)
        {
        }

        /// <inheritdoc />
        public void HandleResponse(InterfaceId functionId, SignalLink.Ack response)
        {
            LastAck = response.Sequence;
        }

        [Timer(1)]
        public void OnTick()
        {
            foreach (var sink in this.GetLinkedSignalSinks())
            {
                this.SendRequest(sink, new SignalLink.Ping(++_sequence));
                SentPings++;
            }
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>Receives pings and counts them on an observable property — the "device" end of the link.</summary>
    [LogicBlock(Name = "Signal Sink", Icon = "device-line")]
    [LogicBlockInterfaceBinding(typeof(ISignalSink), Multiplicity = LinkMultiplicity.ExactlyOne)]
    public class SignalSinkBlock : LogicBlockBase, ISignalSink
    {
        [ServiceProperty(Title = "Empfangene Pings")]
        [Presentation(Group = PropertyGroup.Metric, Importance = Importance.Primary)]
        public int ReceivedPings { get; private set; }

        [ServiceProperty(Title = "Letzte Sequenz")]
        [Presentation(Group = PropertyGroup.Status)]
        public int LastSequence { get; private set; }

        public SignalSinkBlock(ILogger logger) : base(logger)
        {
        }

        /// <inheritdoc />
        public SignalLink.Ack HandleRequest(SignalLink.Ping request)
        {
            ReceivedPings++;
            LastSequence = request.Sequence;
            return new SignalLink.Ack(request.Sequence);
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
}