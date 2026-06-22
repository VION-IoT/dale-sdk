using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using static Vion.Dale.Sdk.Core.LogicBlockBase;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Test-friendly minimal actor context that records messages sent by the logic block.
    ///     Use the provided query/assertion helpers to inspect recorded messages.
    ///     <code>testContext.VerifyServicePropertyChanged(lb => lb.Power, value => Assert.AreEqual(3.5, value));</code>
    ///     <para>
    ///         Hosts a <see cref="FakeTimeProvider" /> as the virtual clock for both <c>UtcNow</c>
    ///         reads (production code that depends on <c>TimeProvider</c>) and for the deadlines
    ///         attached to <c>InvokeSynchronized</c> / <c>InvokeSynchronizedAfter</c> actions. Call
    ///         <see cref="AdvanceTime" /> to move the clock forward and fire actions whose deadlines
    ///         have elapsed; call <see cref="FlushPendingActions" /> for the legacy clock-agnostic
    ///         drain.
    ///     </para>
    /// </summary>
    [PublicApi]
    public class LogicBlockTestContext<TLogicBlock> : IActorContext
        where TLogicBlock : LogicBlockBase
    {
        // 2026-01-01 UTC matches the anchor that existing consumer tests already use; chosen
        // here so the default flows through to blocks that read TimeProvider on construction.
        private static readonly DateTimeOffset DefaultAnchor = new(2026,
                                                                   1,
                                                                   1,
                                                                   0,
                                                                   0,
                                                                   0,
                                                                   TimeSpan.Zero);

        // (Deadline, Action) tuples — deadlines come from the FakeTimeProvider's UtcNow at the
        // moment InvokeSynchronizedAfter was called, plus the requested delay. AdvanceTime
        // dispatches in deadline order; FlushPendingActions ignores the deadline and drains all.
        private readonly List<(DateTimeOffset Deadline, Action Action)> _pendingActions = [];

        private readonly IActorReference _self = new TestActorReference("self");

        private readonly IActorReference _sender = new TestActorReference("sender");

        private readonly List<(IActorReference Target, object Message, Dictionary<string, string>? Headers)> _sentMessages = [];

        // Reentrancy guard so an action fired by AdvanceTime/FlushPendingActions cannot recursively
        // re-enter dispatch — the semantics of nested time-advancement are surprising enough to
        // forbid by default.
        private bool _dispatching;

        // Not readonly: the LogicBlockTestContextBuilder.WithTimeProvider hook swaps this for an
        // externally-owned FakeTimeProvider so tests can construct their block with the same
        // instance they then bind to the test context.

        /// <summary>
        ///     The virtual clock backing this test context. Inject as <see cref="System.TimeProvider" />
        ///     into your logic block to make its <c>UtcNow</c> reads deterministic.
        /// </summary>
        public FakeTimeProvider TimeProvider { get; private set; }

        /// <summary>
        ///     Current virtual time. Shorthand for <c>TimeProvider.GetUtcNow().UtcDateTime</c>.
        /// </summary>
        public DateTime VirtualNow
        {
            get => TimeProvider.GetUtcNow().UtcDateTime;
        }

        public LogicBlockTestContext() : this(DefaultAnchor)
        {
        }

        public LogicBlockTestContext(DateTimeOffset virtualNowAnchor)
        {
            TimeProvider = new FakeTimeProvider(virtualNowAnchor);
        }

        /// <summary>
        ///     Assert that a SendCommand call was made with the given target and message.
        ///     <code>testContext.VerifySendCommand&lt;PingRequest&gt;(mappedPong, msg => Assert.AreEqual(42, msg.Value));</code>
        /// </summary>
        public void VerifySendCommand<TMessage>(InterfaceId? to = null, Action<TMessage>? assertMessage = null, Times? times = null)
            where TMessage : struct
        {
            VerifySendFunctionInterfaceMessage("SendCommand", to, assertMessage, times);
        }

        /// <summary>
        ///     Assert that a SendRequest call was made with the given target and message.
        ///     <code>testContext.VerifySendRequest&lt;PingRequest&gt;(mappedPong, msg => Assert.AreEqual(42, msg.Value));</code>
        /// </summary>
        public void VerifySendRequest<TMessage>(InterfaceId? to = null, Action<TMessage>? assertMessage = null, Times? times = null)
            where TMessage : struct
        {
            VerifySendFunctionInterfaceMessage("SendRequest", to, assertMessage, times);
        }

        /// <summary>
        ///     Assert that a SendStateUpdate call was made with the given message.
        ///     <code>testContext.VerifySendStateUpdate&lt;MyState&gt;(msg => Assert.AreEqual(expected, msg.Value));</code>
        /// </summary>
        public void VerifySendStateUpdate<TMessage>(Action<TMessage>? assertMessage = null, Times? times = null)
            where TMessage : struct
        {
            VerifySendFunctionInterfaceMessage("SendStateUpdate", null, assertMessage, times);
        }

        /// <summary>
        ///     Assert that a service property change was recorded for the specified property.
        ///     <code>testContext.VerifyServicePropertyChanged(lb => lb.Power, value => Assert.AreEqual(3.5, value));</code>
        /// </summary>
        public void VerifyServicePropertyChanged<TValue>(Expression<Func<TLogicBlock, TValue>> propertySelector, Action<TValue>? assertValue = null, Times? times = null)
        {
            var propertyName = GetPropertyName(propertySelector);
            var messages = GetSentMessagesOfType<ServicePropertyValueChanged>().Where(m => m.PropertyIdentifier == propertyName).ToList();

            times ??= Times.Once();
            times.Value.AssertCount(messages.Count, $"ServicePropertyChanged verification failed for property '{propertyName}'");

            if (assertValue != null)
            {
                foreach (var message in messages)
                {
                    assertValue((TValue)message.Value!);
                }
            }
        }

        /// <summary>
        ///     Assert that a service measuring point change was recorded for the specified property.
        ///     <code>testContext.VerifyServiceMeasuringPointChanged(lb => lb.Temperature, value => Assert.AreEqual(22.5, value));</code>
        /// </summary>
        public void VerifyServiceMeasuringPointChanged<TValue>(Expression<Func<TLogicBlock, TValue>> propertySelector, Action<TValue>? assertValue = null, Times? times = null)
        {
            var propertyName = GetPropertyName(propertySelector);
            var messages = GetSentMessagesOfType<ServiceMeasuringPointValueChanged>().Where(m => m.MeasuringPointIdentifier == propertyName).ToList();

            times ??= Times.Once();
            times.Value.AssertCount(messages.Count, $"ServiceMeasuringPointChanged verification failed for property '{propertyName}'");

            if (assertValue != null)
            {
                foreach (var message in messages)
                {
                    assertValue((TValue)message.Value!);
                }
            }
        }

        public void VerifyContractMessageSent<TData>(string messageKind, string? contractIdentifier = null, Func<TData, bool>? verifyMessage = null, Times? times = null)
            where TData : struct
        {
            var items = GetSentMessagesOfType<ContractMessage<TData>>()
                        .Where(m => (contractIdentifier == null || m.LogicBlockContractId.ContractIdentifier.Equals(contractIdentifier)) &&
                                    (verifyMessage == null || verifyMessage(m.Data)))
                        .ToList();

            times ??= Times.Once();
            times.Value.AssertCount(items.Count, $"{messageKind} verification failed for type {typeof(TData).FullName}");
        }

        /// <summary>
        ///     Returns all recorded contract messages of the specified data type, optionally filtered by contract identifier.
        ///     Useful for TestKit extensions that need to extract pending requests for response simulation.
        /// </summary>
        public IReadOnlyList<ContractMessage<TData>> GetContractMessages<TData>(string? contractIdentifier = null)
            where TData : struct
        {
            return _sentMessages.Where(s => s.Message is ContractMessage<TData>)
                                .Select(s => (ContractMessage<TData>)s.Message)
                                .Where(m => contractIdentifier == null || m.LogicBlockContractId.ContractIdentifier == contractIdentifier)
                                .ToList();
        }

        /// <summary>
        ///     Clear recorded messages, e.g. if the test arranging phase triggers messages, that should be ignored.
        /// </summary>
        public void ClearRecordedMessages()
        {
            _sentMessages.Clear();
        }

        /// <summary>
        ///     Advance the virtual clock by <paramref name="delta" /> and dispatch every queued action
        ///     whose deadline has elapsed, in deadline order. The clock is set to each action's deadline
        ///     immediately before that action runs, so an action's own <c>UtcNow</c> read sees the time
        ///     it was scheduled for (not the post-advance target). Actions queued during dispatch with a
        ///     deadline still ≤ the target time fire in the same call (cascading); actions whose
        ///     deadline lies beyond the target stay pending for a later <c>AdvanceTime</c>.
        /// </summary>
        public void AdvanceTime(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "AdvanceTime cannot move the clock backwards.");
            }

            if (_dispatching)
            {
                throw new
                    InvalidOperationException("AdvanceTime cannot be called recursively from within a fired action. Schedule follow-up work via InvokeSynchronizedAfter and let the outer AdvanceTime cascade.");
            }

            var target = TimeProvider.GetUtcNow() + delta;
            _dispatching = true;
            try
            {
                while (true)
                {
                    // Pick the next action whose deadline is in [now, target]. By repeatedly
                    // finding the minimum we get deterministic deadline ordering even when actions
                    // queued during dispatch land between already-due peers.
                    var nextIndex = -1;
                    var nextDeadline = DateTimeOffset.MaxValue;
                    for (var i = 0; i < _pendingActions.Count; i++)
                    {
                        var d = _pendingActions[i].Deadline;
                        if (d <= target && d < nextDeadline)
                        {
                            nextIndex = i;
                            nextDeadline = d;
                        }
                    }

                    if (nextIndex < 0)
                    {
                        break;
                    }

                    var (deadline, action) = _pendingActions[nextIndex];
                    _pendingActions.RemoveAt(nextIndex);
                    TimeProvider.SetUtcNow(deadline);
                    action();
                }

                // Land the clock at the requested target, regardless of where the last dispatched
                // action set it. Without this, AdvanceTime(10s) on an empty queue would be a no-op
                // and the clock would lag the caller's intent.
                TimeProvider.SetUtcNow(target);
            }
            finally
            {
                _dispatching = false;
            }
        }

        /// <summary>
        ///     Execute every action currently queued by <see cref="LogicBlockBase.InvokeSynchronized" />
        ///     or <see cref="LogicBlockBase.InvokeSynchronizedAfter" />, ignoring their scheduled
        ///     deadlines and ignoring the virtual clock. New code that wants deterministic time
        ///     semantics should prefer <see cref="AdvanceTime" />; this method exists for tests that
        ///     just want to drain the queue without caring about elapsed simulated time.
        ///     <code>
        ///     sut.OnTimer();                          // sends requests, queues Calculate
        ///     sut.HandleResponse(id, response);       // feed response data
        ///     testContext.FlushPendingActions();       // now Calculate() runs
        ///     </code>
        ///     <para>
        ///         The drain is single-pass: actions queued by an action that runs during this call
        ///         are deferred to the next <c>FlushPendingActions</c> call. A self-rescheduling tick
        ///         will therefore fire exactly once per call, not loop until the queue is empty.
        ///     </para>
        /// </summary>
        public void FlushPendingActions()
        {
            if (_dispatching)
            {
                throw new InvalidOperationException("FlushPendingActions cannot be called recursively from within a fired action.");
            }

            var snapshot = _pendingActions.ToList();
            _pendingActions.Clear();
            _dispatching = true;
            try
            {
                foreach (var (_, action) in snapshot)
                {
                    action();
                }
            }
            finally
            {
                _dispatching = false;
            }
        }

        /// <summary>
        ///     The service provider the block was initialized with. Set by the builder after
        ///     <c>BuildServiceProvider</c> completes so tests can assert which registrations the
        ///     builder applied (e.g. <see cref="Vion.Dale.Sdk.Emission.EmissionPolicyForceMarker" />
        ///     when <c>WithEmissionPolicy(FromAttributes)</c> was called).
        /// </summary>
        public IServiceProvider? BuiltServiceProvider { get; internal set; }

        /// <summary>
        ///     Internal swap point used by <c>LogicBlockTestContextBuilder.WithTimeProvider</c> so
        ///     the same FakeTimeProvider can be passed to the block's constructor and then bound to
        ///     the test context, instead of the two clocks drifting independently.
        /// </summary>
        internal void SetTimeProvider(FakeTimeProvider timeProvider)
        {
            TimeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        private void VerifySendFunctionInterfaceMessage<TMessage>(string messageKind, InterfaceId? to, Action<TMessage>? assertMessage, Times? times)
            where TMessage : struct
        {
            var items = GetSentMessagesOfType<FunctionInterfaceMessage<TMessage>>().Where(m => !to.HasValue || m.ToId.Equals(to.Value)).ToList();

            times ??= Times.Once();
            times.Value.AssertCount(items.Count, $"{messageKind} verification failed for type {typeof(TMessage).FullName}");

            if (assertMessage != null)
            {
                foreach (var item in items)
                {
                    assertMessage(item.Data);
                }
            }
        }

        private static string GetPropertyName<TValue>(Expression<Func<TLogicBlock, TValue>> propertySelector)
        {
            var expression = propertySelector.Body;

            // Unwrap Convert node added by boxing for value types
            if (expression is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            {
                expression = unary.Operand;
            }

            if (expression is MemberExpression { Member: PropertyInfo property })
            {
                return property.Name;
            }

            throw new ArgumentException("Expression must be a property access, e.g. lb => lb.MyProperty", nameof(propertySelector));
        }

        private IEnumerable<TMessage> GetSentMessagesOfType<TMessage>()
        {
            return _sentMessages.Where(s => s.Message is TMessage).Select(s => (TMessage)s.Message);
        }

        #region IActorContext implementation, explicit to hide in tests

        IReadOnlyDictionary<string, string>? IActorContext.Headers
        {
            get => null;
        }

        IActorReference IActorContext.LookupByName(string name)
        {
            return new TestActorReference(name);
        }

        void IActorContext.SendTo(IActorReference target, object message, Dictionary<string, string>? headers)
        {
            _sentMessages.Add((target, message, headers));
        }

        void IActorContext.SendToSelf(object message)
        {
            _sentMessages.Add((_self, message, null));

            // Mirror SendToSelfAfter's drain enlistment: in production the runtime dispatches
            // InvokeActionMessage on its next message-pump iteration; in TestKit there is no pump,
            // so we enlist with deadline = now so FlushPendingActions / AdvanceTime(>=0) drain it.
            // Without this, LogicBlockBase.InvokeSynchronized(action) is silently lost under the
            // TestKit (whereas InvokeSynchronizedAfter(action, TimeSpan.Zero) would work).
            if (message is InvokeActionMessage actionMessage)
            {
                _pendingActions.Add((TimeProvider.GetUtcNow(), actionMessage.Action));
            }
        }

        void IActorContext.SendToSelfAfter(object message, TimeSpan delay)
        {
            _sentMessages.Add((_self, message, null));

            if (message is InvokeActionMessage actionMessage)
            {
                _pendingActions.Add((TimeProvider.GetUtcNow() + delay, actionMessage.Action));
            }
        }

        void IActorContext.RespondToSender(object message)
        {
            _sentMessages.Add((_sender, message, null));
        }

        #endregion
    }
}