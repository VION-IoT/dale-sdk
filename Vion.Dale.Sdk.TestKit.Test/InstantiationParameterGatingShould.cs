using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.TestKit.Test
{
    public sealed class GatedStationComponent
    {
        [ServiceProperty(Title = "Aktiv")]
        public bool Active { get; set; }
    }

    public sealed class TestKitGatedBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 3)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        public GatedStationComponent Point1 { get; } = new();

        [IncludedWhen("PointCount >= 2")]
        public GatedStationComponent Point2 { get; } = new();

        [IncludedWhen("PointCount >= 3")]
        public GatedStationComponent Point3 { get; } = new();

        public TestKitGatedBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     The TestKit's own entry to the config-time value channel
    ///     (<c>docs/specs/config-gating.md</c>): a test chooses parameter values the way a configuration does,
    ///     so the gates a block author wrote are exercised through the encode and decode path that ships.
    /// </summary>
    [TestClass]
    public sealed class InstantiationParameterGatingShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.1")]
        public void ApplyParameterValueThroughConfigurationPath()
        {
            // Setting the property directly would skip the decode the configuration channel performs, so the
            // test would not exercise what ships.

            // Arrange
            var block = new TestKitGatedBlock();

            // Act
            var context = block.CreateTestContext().WithInstantiationParameter(lb => lb.PointCount, 2).WithoutAutoStart().Build();

            // Assert
            Assert.AreEqual(2, block.PointCount);
            CollectionAssert.AreEquivalent(new[] { nameof(TestKitGatedBlock), nameof(TestKitGatedBlock.Point1), nameof(TestKitGatedBlock.Point2) }, BoundServices(context));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.1")]
        public void ResolveGatesAgainstDeclaredDefaultWhenNoValueSupplied()
        {
            // Arrange
            var block = new TestKitGatedBlock();

            // Act
            var context = block.CreateTestContext().WithoutAutoStart().Build();

            // Assert
            Assert.AreEqual(1, block.PointCount);
            CollectionAssert.AreEquivalent(new[] { nameof(TestKitGatedBlock), nameof(TestKitGatedBlock.Point1) }, BoundServices(context));
        }

        // The bound service set as the block itself announced it, rather than by reflecting into its binder.
        // Both tests build WithoutAutoStart because starting clears the recorded messages, the announcement
        // among them — and the gates this asserts on resolved before the block ever started.
        private static string[] BoundServices(LogicBlockTestContext<TestKitGatedBlock> context)
        {
            return context.GetSentMessagesOfTypePublic<BindLogicBlockServices>().Last().Properties.Keys.Select(identifier => identifier.Id).ToArray();
        }
    }
}