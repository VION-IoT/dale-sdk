using System;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     Where a struct-field declaration may be written (<c>docs/specs/introspection.md</c>). The target
    ///     set is the whole behavior: both introspection readers walk a struct's positional constructor, so
    ///     a declaration anywhere else is one nothing reads and no diagnostic judges.
    /// </summary>
    [TestClass]
    public class StructFieldAttributeShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.8")]
        public void BeDeclarableOnConstructorParametersOnly()
        {
            // Arrange
            var attribute = typeof(StructFieldAttribute);

            // Act
            var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>();

            // Assert
            Assert.IsNotNull(usage);
            Assert.AreEqual(AttributeTargets.Parameter, usage.ValidOn);
        }
    }
}