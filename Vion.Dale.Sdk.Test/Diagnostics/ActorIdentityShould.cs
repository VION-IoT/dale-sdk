using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.Sdk.Test.Diagnostics
{
    [TestClass]
    public class ActorIdentityShould
    {
        [TestMethod]
        public void ClassifyAnActorWithTheLogicBlockPrefixAsALogicBlock()
        {
            var identity = ActorIdentity.For(typeof(SampleBlock), "logicblock_Heater_abc123");

            Assert.AreEqual(ActorCategory.LogicBlock, identity.Category);
            Assert.AreEqual(nameof(SampleBlock), identity.Type);
            Assert.AreEqual(typeof(SampleBlock).Assembly.GetName().Name, identity.Library);
        }

        [TestMethod]
        public void ClassifyAnActorWithoutThePrefixAsRuntime()
        {
            var identity = ActorIdentity.For(typeof(SampleBlock), "MockServicePropertyHandler");

            Assert.AreEqual(ActorCategory.Runtime, identity.Category);
            Assert.AreEqual(nameof(SampleBlock), identity.Type);
            Assert.IsNull(identity.Library);
        }

        private sealed class SampleBlock
        {
        }
    }
}