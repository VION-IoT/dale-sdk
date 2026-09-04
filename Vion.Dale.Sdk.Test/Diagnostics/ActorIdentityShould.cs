using Vion.Dale.Sdk.Diagnostics;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Diagnostics
{
    /// <summary>
    ///     How an actor's name becomes the dimensions its vitals are reported under. The prefix is the whole
    ///     of the classification, which is why a host can also find its blocks by scanning the registry for
    ///     it.
    /// </summary>
    [TestClass]
    public sealed class ActorIdentityShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-LIFE-013.1")]
        public void BuildActorNameFromPrefixAndBlocksNameAndIdentifier()
        {
            // Act
            var name = LogicBlockUtils.CreateLogicBlockName("Heater", "abc123");

            // Assert
            Assert.AreEqual(LogicBlockUtils.LogicBlockPrefix + "Heater_abc123", name, "Every registry scan, every stop and every vitals tag keys on this shape.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-013.2")]
        public void ClassifyActorCarryingPrefixAsLogicBlock()
        {
            // Act
            var identity = ActorIdentity.For(typeof(SampleBlock), LogicBlockUtils.CreateLogicBlockName("Heater", "abc123"));

            // Assert
            Assert.AreEqual(ActorCategory.LogicBlock, identity.Category, "The prefix alone decides it — nothing about the type is consulted.");
            Assert.AreEqual(nameof(SampleBlock), identity.Type, "A block reports its class, which is what the fleet tier aggregates on.");
            Assert.AreEqual(typeof(SampleBlock).Assembly.GetName().Name, identity.Library, "And the library it was published from.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-013.2")]
        public void ClassifyActorWithoutPrefixAsRuntime()
        {
            // Act
            var identity = ActorIdentity.For(typeof(SampleBlock), "MockServicePropertyHandler");

            // Assert
            Assert.AreEqual(ActorCategory.Runtime, identity.Category, "The runtime's own actors go through the same spawn seam and are told apart by their names.");
            Assert.AreEqual(nameof(SampleBlock), identity.Type, "A runtime actor reports its class as its role.");
            Assert.IsNull(identity.Library, "And no library — it came from no plugin.");
        }

        private sealed class SampleBlock
        {
        }
    }
}