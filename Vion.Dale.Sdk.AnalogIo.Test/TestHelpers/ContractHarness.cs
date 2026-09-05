using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.Test.TestHelpers
{
    /// <summary>
    ///     Stands a contract face up the way the runtime does — an identity, then a linked handler actor — and
    ///     records what it sends. A face constructed without both is the unmapped and the unlinked case, which
    ///     the suites arrange by leaving one of them out.
    /// </summary>
    internal sealed class ContractHarness
    {
        internal const string BlockIdValue = "lb0";

        private readonly Mock<IActorReference> _handlerActorMock = new();

        internal Mock<IActorContext> ActorContextMock { get; } = new();

        /// <summary>Every message a face handed the context, in order.</summary>
        internal List<object> Sent { get; } = [];

        internal ContractHarness()
        {
            ActorContextMock.Setup(context => context.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()))
                            .Callback<IActorReference, object, Dictionary<string, string>?>((_, message, _) => Sent.Add(message));
        }

        internal AnalogInput Input(string identifier = "ai0")
        {
            return Wire(new AnalogInput(identifier, ActorContextMock.Object, NullLogger<AnalogInput>.Instance), identifier);
        }

        internal AnalogOutput Output(string identifier = "ao0")
        {
            return Wire(new AnalogOutput(identifier, ActorContextMock.Object, NullLogger<AnalogOutput>.Instance), identifier);
        }

        internal AnalogInputProvider InputProvider(string identifier = "aip0")
        {
            return Wire(new AnalogInputProvider(identifier, ActorContextMock.Object, NullLogger<AnalogInputProvider>.Instance), identifier);
        }

        internal AnalogOutputProvider OutputProvider(string identifier = "aop0")
        {
            return Wire(new AnalogOutputProvider(identifier, ActorContextMock.Object, NullLogger<AnalogOutputProvider>.Instance), identifier);
        }

        /// <summary>A face the configuration never mapped — no logic-block identity was ever set on it.</summary>
        internal AnalogOutput UnmappedOutput(string identifier = "ao0")
        {
            return new AnalogOutput(identifier, ActorContextMock.Object, NullLogger<AnalogOutput>.Instance);
        }

        /// <summary>Hands a face the message its handler would deliver.</summary>
        internal static void Deliver<TData>(LogicBlockContractBase contract, string identifier, TData data)
            where TData : struct
        {
            contract.HandleContractMessage(new ContractMessage<TData>(new LogicBlockContractId(new LogicBlockId(BlockIdValue), identifier), data));
        }

        private TContract Wire<TContract>(TContract contract, string identifier)
            where TContract : LogicBlockContractBase
        {
            contract.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId(BlockIdValue), identifier));
            contract.SetLinkedContractHandler(_handlerActorMock.Object);

            return contract;
        }
    }
}