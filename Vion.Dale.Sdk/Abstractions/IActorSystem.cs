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

        /// <summary>
        ///     Sends <paramref name="message" /> to each actor and completes with every actor's acknowledgement,
        ///     keyed by the reference passed.
        /// </summary>
        /// <exception cref="AcknowledgementTimeoutException{TAcknowledgementMessage}">
        ///     Thrown when <paramref name="timeout" /> elapses before every actor has answered, carrying the
        ///     acknowledgements that arrived and the actors that did not answer.
        /// </exception>
        Task<Dictionary<IActorReference, TAcknowledgementMessage>> SendAndWaitForAcknowledgementAsync<TRequestMessage, TAcknowledgementMessage>(
            List<IActorReference> actors,
            TRequestMessage message,
            TimeSpan timeout)
            where TRequestMessage : struct
            where TAcknowledgementMessage : struct;

        /// <summary>
        ///     Sends each actor its own message and completes with every actor's acknowledgement, keyed by the
        ///     reference passed.
        /// </summary>
        /// <exception cref="AcknowledgementTimeoutException{TAcknowledgementMessage}">
        ///     Thrown when <paramref name="timeout" /> elapses before every actor has answered, carrying the
        ///     acknowledgements that arrived and the actors that did not answer.
        /// </exception>
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