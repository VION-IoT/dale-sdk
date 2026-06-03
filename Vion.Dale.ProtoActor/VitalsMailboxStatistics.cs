using Proto.Mailbox;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.ProtoActor
{
    /// <summary>
    ///     Proto mailbox statistics that feed an actor's mailbox depth into the vitals core. Depth is derived
    ///     by the core as posted − received. One instance per actor (constructed at the spawn site with the
    ///     actor's name); coexists with Proto's own statistics and stays off the OTel export path.
    /// </summary>
    public sealed class VitalsMailboxStatistics : IMailboxStatistics
    {
        private readonly string _actorName;
        private readonly IActorVitalsCollector _collector;

        public VitalsMailboxStatistics(string actorName, IActorVitalsCollector collector)
        {
            _actorName = actorName;
            _collector = collector;
        }

        public void MailboxStarted()
        {
        }

        public void MessagePosted(object message)
        {
            _collector.OnMessagePosted(_actorName);
        }

        public void MessageReceived(object message)
        {
            _collector.OnMessageReceived(_actorName);
        }

        public void MailboxEmpty()
        {
        }
    }
}
