using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Abstractions
{
    public interface IActorSystem
    {
        void SendTo<TMessage>(IActorReference target, TMessage message)
            where TMessage : struct;

        Task<Dictionary<IActorReference, TAcknowledgementMessage>> SendAndWaitForAcknowledgementAsync<TRequestMessage, TAcknowledgementMessage>(
            List<IActorReference> actors,
            TRequestMessage message,
            TimeSpan timeout)
            where TRequestMessage : struct
            where TAcknowledgementMessage : struct;

        Task<Dictionary<IActorReference, TAcknowledgementMessage>> SendAndWaitForAcknowledgementAsync<TRequestMessage, TAcknowledgementMessage>(
            Dictionary<IActorReference, TRequestMessage> actorMessages,
            TimeSpan timeout)
            where TRequestMessage : struct
            where TAcknowledgementMessage : struct;

        IActorReference CreateRootActorFor<TActorReceiver>(Func<TActorReceiver> factory, string name, object? logger = null)
            where TActorReceiver : IActorReceiver;

        IActorReference CreateRootActorFromDi(Type actorReceiverType, string name, ILogger? logger = null);

        IActorReference CreateRootActorFromDi<T>(string name, ILogger? logger = null);

        Task StopActorsAndWaitAsync(List<IActorReference> actorsToStop, TimeSpan timeout);

        Task ShutdownAsync();

        List<IActorReference> FindByName(Regex actorNameRegex);

        IActorReference LookupByName(string name);
    }
}