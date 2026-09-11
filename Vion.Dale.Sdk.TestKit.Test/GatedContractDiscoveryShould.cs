using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>A service-provider contract for the gating suite, so a gated property has a type to carry.</summary>
    [ServiceProviderContractType("TestKitGatingProbe")]
    public interface ITestKitGatingProbe
    {
        void Poke(int amount);
    }

    /// <inheritdoc cref="ITestKitGatingProbe" />
    public sealed class TestKitGatingProbeContract : LogicBlockContractBase, ITestKitGatingProbe
    {
        public override string ContractHandlerActorName { get; protected set; } = "TestKitGatingProbeHandler";

        public TestKitGatingProbeContract(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public void Poke(int amount)
        {
        }

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage message)
        {
        }
    }

    /// <summary>
    ///     One ungated contract and one gated on the same parameter the service suite gates on, so a single
    ///     build shows both halves: the gated-out property must not be mapped, the ungated one must.
    /// </summary>
    public sealed class TestKitGatedContractBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 3)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        // Nullable and uninitialised, like every other contract property in the suites: the binder is
        // what writes one, so a non-null initializer would let the cleanup profile take the setter off.
        public ITestKitGatingProbe? FirstProbe { get; private set; }

        [IncludedWhen("PointCount >= 2")]
        public ITestKitGatingProbe? SecondProbe { get; private set; }

        public TestKitGatedContractBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     The kit's contract-id discovery against the inclusion gate the binders resolve
    ///     (<c>docs/specs/config-gating.md</c>). A gated-out contract is never bound and the cloud refuses a
    ///     mapping for one at activation, so a kit that mapped every contract property let an author's
    ///     gating test pass against a shape the deployment target rejects.
    /// </summary>
    [TestClass]
    public sealed class GatedContractDiscoveryShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.13")]
        public void OmitContractExcludedByGate()
        {
            // The mapping set itself, not the block's state afterwards: a gated-out contract property is
            // left null by the BINDER whether or not discovery mapped it, so an assertion on the property
            // passes with this fix reverted.

            // Arrange
            var builder = new TestKitGatedContractBlock().CreateTestContext();

            // Act
            var mapped = builder.DiscoverContractIds();

            // Assert
            CollectionAssert.AreEquivalent(new[] { nameof(TestKitGatedContractBlock.FirstProbe) }, mapped.Keys.ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.13")]
        public void KeepContractIncludedByGate()
        {
            // Arrange
            var builder = new TestKitGatedContractBlock().CreateTestContext().WithInstantiationParameter(lb => lb.PointCount, 2);

            // Act
            var mapped = builder.DiscoverContractIds();

            // Assert
            CollectionAssert.AreEquivalent(new[] { nameof(TestKitGatedContractBlock.FirstProbe), nameof(TestKitGatedContractBlock.SecondProbe) }, mapped.Keys.ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.13")]
        public void MapContractIdOfEveryKeptBinding()
        {
            // The kept half end to end: dropping the gated entry must not drop the entry beside it, and the
            // id the block receives must reach the contract it binds.

            // Arrange
            var block = new TestKitGatedContractBlock();

            // Act
            block.CreateTestContext().WithoutAutoStart().Build();

            // Assert
            Assert.IsNotNull(block.FirstProbe);
            Assert.IsNull(block.SecondProbe);
            Assert.AreEqual(nameof(TestKitGatedContractBlock.FirstProbe), ((LogicBlockContractBase)block.FirstProbe).Identifier);
        }
    }
}