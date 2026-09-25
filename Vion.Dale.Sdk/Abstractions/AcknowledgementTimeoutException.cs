using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Thrown when an acknowledgement wait's timeout elapses before every actor has answered. It carries
    ///     the answers that did arrive and the actors that stayed silent, so a caller can act on the ones
    ///     that answered and name the ones that did not.
    /// </summary>
    /// <typeparam name="TAcknowledgementMessage">The acknowledgement the wait was collecting.</typeparam>
    /// <remarks>
    ///     It is a <see cref="TimeoutException" />, so a caller that only needs to know the wait failed catches
    ///     that.
    /// </remarks>
    public class AcknowledgementTimeoutException<TAcknowledgementMessage> : TimeoutException
        where TAcknowledgementMessage : struct
    {
        /// <summary>
        ///     Gets the acknowledgements that arrived before the timeout, keyed by the references the caller
        ///     passed.
        /// </summary>
        public IReadOnlyDictionary<IActorReference, TAcknowledgementMessage> Acknowledgements { get; }

        /// <summary>
        ///     Gets the references the caller passed whose actors had not answered when the timeout elapsed.
        /// </summary>
        public IReadOnlyList<IActorReference> Unanswered { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="AcknowledgementTimeoutException{TAcknowledgementMessage}" />
        ///     class.
        /// </summary>
        /// <param name="acknowledgements">The acknowledgements that arrived, keyed by the caller's references.</param>
        /// <param name="unanswered">The caller's references whose actors did not answer.</param>
        public AcknowledgementTimeoutException(IReadOnlyDictionary<IActorReference, TAcknowledgementMessage> acknowledgements, IReadOnlyList<IActorReference> unanswered) :
            base($"Timeout waiting for {unanswered.Count} actor(s) to acknowledge")
        {
            Acknowledgements = acknowledgements;
            Unanswered = unanswered;
        }
    }
}
