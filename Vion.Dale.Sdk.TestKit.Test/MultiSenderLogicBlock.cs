using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    // Two minimal contract interfaces decorated with [LogicInterface] so the TestKit's
    // ambiguity guard sees them. Self-referencing type slots (MatchingInterface,
    // SenderInterface, ContractType) keep the fixture standalone — we never call Build()
    // on this block, so the binder never inspects those properties.

    [LogicInterface(MatchingInterface = typeof(IFakeContractA), SenderInterface = typeof(IFakeContractA), ContractType = typeof(IFakeContractA))]
    public interface IFakeContractA : ILogicHandlerInterface
    {
    }

    [LogicInterface(MatchingInterface = typeof(IFakeContractB), SenderInterface = typeof(IFakeContractB), ContractType = typeof(IFakeContractB))]
    public interface IFakeContractB : ILogicHandlerInterface
    {
    }

    /// <summary>
    ///     A block that implements two contract interfaces. Used to exercise the ambiguity
    ///     guard in WithLogicInterfaceMapping — without the guard, two bare
    ///     <c>WithLogicInterfaceMapping(lb =&gt; lb, idX)</c> calls collapse onto the same
    ///     dictionary key and route to whichever contract comes first in metadata order,
    ///     silently dropping the second mapping.
    /// </summary>
    public class MultiSenderLogicBlock : LogicBlockBase, IFakeContractA, IFakeContractB
    {
        public MultiSenderLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     A block that implements exactly one contract interface. Confirms the guard
    ///     permits the bare-lambda form when there is no ambiguity to detect.
    /// </summary>
    public class SingleSenderLogicBlock : LogicBlockBase, IFakeContractA
    {
        public SingleSenderLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }
}