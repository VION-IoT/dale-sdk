using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.TestKit.Test
{
    [TestClass]
    public class WithEmissionPolicyShould
    {
        [TestMethod]
        public void RegisterForceMarker_WhenFromAttributes()
        {
            // WithEmissionPolicy(FromAttributes) must put EmissionPolicyForceMarker into the same
            // provider the block reads at InitializeLogicBlock. We verify via
            // LogicBlockTestContext.BuiltServiceProvider, which is the exact IServiceProvider passed
            // to InitializeLogicBlock — proving the registration reached init.
            var block = LogicBlockTestHelper.Create<ThrottledLogicBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            Assert.IsNotNull(ctx.BuiltServiceProvider!.GetService(typeof(EmissionPolicyForceMarker)),
                             "WithEmissionPolicy(FromAttributes) must register EmissionPolicyForceMarker so the block sees it at init.");
        }

        [TestMethod]
        public void NotRegisterForceMarker_ByDefault()
        {
            // Default builder leaves policy OFF: no marker in the provider the block reads.
            var block = LogicBlockTestHelper.Create<ThrottledLogicBlock>();
            var ctx = block.CreateTestContext().Build();

            Assert.IsNull(ctx.BuiltServiceProvider!.GetService(typeof(EmissionPolicyForceMarker)),
                          "Default builder must NOT register the force marker.");
        }

        [TestMethod]
        public void NotRegisterForceMarker_WhenOffExplicitly()
        {
            // Passing Off explicitly behaves like the default.
            var block = LogicBlockTestHelper.Create<ThrottledLogicBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.Off).Build();

            Assert.IsNull(ctx.BuiltServiceProvider!.GetService(typeof(EmissionPolicyForceMarker)),
                          "WithEmissionPolicy(Off) must not register the force marker.");
        }

        [TestMethod]
        public void ReturnBuilderForChaining()
        {
            var block = LogicBlockTestHelper.Create<ThrottledLogicBlock>();
            var builder = block.CreateTestContext();

            var returned = builder.WithEmissionPolicy(EmissionPolicyMode.FromAttributes);

            Assert.AreSame(builder, returned);
        }
    }
}
