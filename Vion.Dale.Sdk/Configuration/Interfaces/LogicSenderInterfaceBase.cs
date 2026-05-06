using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    public abstract class LogicSenderInterfaceBase : ILogicSenderInterface
    {
        private readonly IActorContext _actorContext;

        private readonly ILogger _logger;

        private readonly Func<LogicBlockId> _logicBlockId;

        private Dictionary<InterfaceId, IActorReference> _linkedFunctions = [];

        public string Identifier { get; }

        public FunctionInterfaceMetaData MetaData { get; } = new();

        private InterfaceId FunctionId
        {
            get => new(_logicBlockId(), Identifier);
        }

        public Type MatchingLogicInterfaceType { get; }

        protected LogicSenderInterfaceBase(string identifier,
                                           Type logicInterfaceType,
                                           Type matchingLogicInterfaceType,
                                           Func<LogicBlockId> logicBlockId,
                                           IActorContext actorContext,
                                           ILogger logger)
        {
            Identifier = identifier;
            LogicInterfaceType = logicInterfaceType;
            MatchingLogicInterfaceType = matchingLogicInterfaceType;
            _logicBlockId = logicBlockId;
            _actorContext = actorContext;
            _logger = logger;
        }

        public IReadOnlyCollection<InterfaceId> LinkedInterfaceIds
        {
            get => _linkedFunctions.Keys.ToHashSet();
        }

        public Type LogicInterfaceType { get; }

        public void SetLinkedInterfaceIds(Dictionary<InterfaceId, IActorReference> linkedFunctions)
        {
            _linkedFunctions = linkedFunctions;
        }

        public abstract void HandleMessage(IFunctionInterfaceMessage functionInterfaceMessage);

        protected void SendToFunction<T>(InterfaceId toId, T data)
            where T : struct
        {
            if (_linkedFunctions.TryGetValue(toId, out var logicBlockRef))
            {
                _actorContext.SendTo(logicBlockRef, new FunctionInterfaceMessage<T>(FunctionId, toId, data));
            }
            else
            {
                _logger.LogWarning("SendToLinkedFunction failed. Linked function {FunctionId} not found", toId);
            }
        }

        protected void SendToAllLinkedFunctions<T>(T data)
            where T : struct
        {
            foreach (var (linkedFunctionId, logicBlockRef) in _linkedFunctions)
            {
                _actorContext.SendTo(logicBlockRef, new FunctionInterfaceMessage<T>(FunctionId, linkedFunctionId, data));
            }
        }
    }
}
