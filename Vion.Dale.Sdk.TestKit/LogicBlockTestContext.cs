using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
    /// </summary>
    [PublicApi]
    public class LogicBlockTestContext<TLogicBlock> : IActorContext
        where TLogicBlock : LogicBlockBase
    {
        private readonly List<Action> _pendingActions = [];

        private readonly IActorReference _self = new TestActorReference("self");

        private readonly IActorReference _sender = new TestActorReference("sender");

        private readonly List<(IActorReference Target, object Message, Dictionary<string, string>? Headers)> _sentMessages = [];

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
        ///     Execute all actions queued by <see cref="LogicBlockBase.InvokeSynchronizedAfter" />.
        ///     In the real actor system these run after a delay; in tests they are captured and
        ///     executed on demand so you can feed responses between the scheduling and the execution.
        ///     <code>
        ///     sut.OnTimer();                          // sends requests, queues Calculate
        ///     sut.HandleResponse(id, response);       // feed response data
        ///     testContext.FlushPendingActions();       // now Calculate() runs
        ///     </code>
        /// </summary>
        public void FlushPendingActions()
        {
            while (_pendingActions.Count > 0)
            {
                var batch = new List<Action>(_pendingActions);
                _pendingActions.Clear();
                foreach (var action in batch)
                {
                    action();
                }
            }
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
        }

        void IActorContext.SendToSelfAfter(object message, TimeSpan delay)
        {
            _sentMessages.Add((_self, message, null));

            if (message is InvokeActionMessage actionMessage)
            {
                _pendingActions.Add(actionMessage.Action);
            }
        }

        void IActorContext.RespondToSender(object message)
        {
            _sentMessages.Add((_sender, message, null));
        }

        #endregion
    }
}
