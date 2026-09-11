using Microsoft.Extensions.Logging;
using Moq;
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
        public TestKitGatingProbeContract(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public override string ContractHandlerActorName { get; protected set; } = "TestKitGatingProbeHandler";

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
        public TestKitGatedContractBlock(ILogger logger) : base(logger)
        {
        }

        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 3)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        public ITestKitGatingProbe FirstProbe { get; private set; } = null!;

        // Nullable, which is the documented authoring shape for a gated contract: the binder is what
        // constructs one, so a gated-out property is left null.
        [IncludedWhen("PointCount >= 2")]
        public ITestKitGatingProbe? SecondProbe { get; private set; }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     The kit's contract-id discovery against the inclusion gate the binders resolve
    ///     (<c>docs/specs/config-gating.md</c>). A kit that maps every contract property hands the block a
    ///     mapping for a contract the gate excluded — a shape the cloud rejects at activation and no host
    ///     ever produces — so a green gating test in the kit would not mean the real host agrees.
    /// </summary>
    [TestClass]
    public sealed class GatedContractDiscoveryShould
    {
        private const string MappingForUnboundContract = "is not a bound contract";

        private const string ContractWithoutMapping = "has no contract mapping in configuration";

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.13")]
        public void NotMapAContractTheGateExcludes()
        {
            // Arrange
            var logger = new Mock<ILogger>();
            var block = new TestKitGatedContractBlock(logger.Object);

            // Act
            block.CreateTestContext().WithoutAutoStart().Build();

            // Assert
            Assert.IsNull(block.SecondProbe);
            logger.VerifyLogContains(MappingForUnboundContract, LogLevel.Warning, Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.13")]
        public void MapAContractTheGateIncludes()
        {
            // Arrange
            var logger = new Mock<ILogger>();
            var block = new TestKitGatedContractBlock(logger.Object);

            // Act
            block.CreateTestContext().WithInstantiationParameter(lb => lb.PointCount, 2).WithoutAutoStart().Build();

            // Assert
            Assert.IsNotNull(block.SecondProbe);
            logger.VerifyLogContains(ContractWithoutMapping, LogLevel.Warning, Times.Never());
            logger.VerifyLogContains(MappingForUnboundContract, LogLevel.Warning, Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.13")]
        public void KeepMappingTheUngatedContractBesideAnExcludedOne()
        {
            // Dropping the gated entry must not drop the entry beside it: a discovery loop that skipped
            // the wrong property would leave FirstProbe unmapped and this suite's other two cases silent.

            // Arrange
            var logger = new Mock<ILogger>();
            var block = new TestKitGatedContractBlock(logger.Object);

            // Act
            block.CreateTestContext().WithoutAutoStart().Build();

            // Assert
            Assert.IsNotNull(block.FirstProbe);
            logger.VerifyLogContains(ContractWithoutMapping, LogLevel.Warning, Times.Never());
        }
    }
}
