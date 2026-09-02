using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     The builder hook a test reaches for to exercise throttling under the TestKit's own clock. What
    ///     it registers is asserted through <c>BuiltServiceProvider</c> — the very provider handed to
    ///     <c>InitializeLogicBlock</c> — because a registration that does not reach init changes nothing,
    ///     and a builder that silently did nothing would leave every throttling test passing vacuously.
    /// </summary>
    [TestClass]
    public class WithEmissionPolicyShould
    {
        private ThrottledLogicBlock _block = null!;

        [TestInitialize]
        public void Initialize()
        {
            _block = LogicBlockTestHelper.Create<ThrottledLogicBlock>();
        }

        [TestMethod]
        public void RegisterOverrideWhenAskedForAttributePolicy()
        {
            // Arrange / Act
            var context = _block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Assert
            Assert.IsNotNull(context.BuiltServiceProvider!.GetService(typeof(EmissionPolicyForceMarker)));
        }

        [TestMethod]
        public void RegisterNoOverrideByDefault()
        {
            // Arrange / Act
            var context = _block.CreateTestContext().Build();

            // Assert
            Assert.IsNull(context.BuiltServiceProvider!.GetService(typeof(EmissionPolicyForceMarker)));
        }

        [TestMethod]
        public void RegisterNoOverrideWhenTurnedOffExplicitly()
        {
            // Arrange / Act
            var context = _block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.Off).Build();

            // Assert
            Assert.IsNull(context.BuiltServiceProvider!.GetService(typeof(EmissionPolicyForceMarker)));
        }

        [TestMethod]
        public void ReturnBuilderForChaining()
        {
            // Arrange
            var builder = _block.CreateTestContext();

            // Act
            var returned = builder.WithEmissionPolicy(EmissionPolicyMode.FromAttributes);

            // Assert
            Assert.AreSame(builder, returned);
        }
    }
}