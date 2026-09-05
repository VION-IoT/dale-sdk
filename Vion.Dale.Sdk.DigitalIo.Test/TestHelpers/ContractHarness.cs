using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.Test.TestHelpers
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

        internal DigitalInput Input(string identifier = "di0")
        {
            return Wire(new DigitalInput(identifier, ActorContextMock.Object, NullLogger<DigitalInput>.Instance), identifier);
        }

        internal DigitalOutput Output(string identifier = "do0")
        {
            return Wire(new DigitalOutput(identifier, ActorContextMock.Object, NullLogger<DigitalOutput>.Instance), identifier);
        }

        internal DigitalInputProvider InputProvider(string identifier = "dip0")
        {
            return Wire(new DigitalInputProvider(identifier, ActorContextMock.Object, NullLogger<DigitalInputProvider>.Instance), identifier);
        }

        internal DigitalOutputProvider OutputProvider(string identifier = "dop0")
        {
            return Wire(new DigitalOutputProvider(identifier, ActorContextMock.Object, NullLogger<DigitalOutputProvider>.Instance), identifier);
        }

        /// <summary>A face the configuration never mapped — no logic-block identity was ever set on it.</summary>
        internal DigitalOutput UnmappedOutput(string identifier = "do0")
        {
            return new DigitalOutput(identifier, ActorContextMock.Object, NullLogger<DigitalOutput>.Instance);
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