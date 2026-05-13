using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [LogicBlock(Name = "TestBlock", Icon = "test-icon", Groups = new[] { PropertyGroup.Alarm, PropertyGroup.Status })]
    public class LogicBlockAttributeFixtureBlock
    {
    }

    [LogicBlock]
    public class LogicBlockAttributeBareBlock
    {
    }

    [TestClass]
    public class LogicBlockAttributeShould
    {
        [TestMethod]
        public void CarryName()
        {
            var attr = typeof(LogicBlockAttributeFixtureBlock).GetCustomAttribute<LogicBlockAttribute>()!;
            Assert.AreEqual("TestBlock", attr.Name);
        }

        [TestMethod]
        public void CarryIcon()
        {
            var attr = typeof(LogicBlockAttributeFixtureBlock).GetCustomAttribute<LogicBlockAttribute>()!;
            Assert.AreEqual("test-icon", attr.Icon);
        }

        [TestMethod]
        public void CarryGroupsArray()
        {
            var attr = typeof(LogicBlockAttributeFixtureBlock).GetCustomAttribute<LogicBlockAttribute>()!;
            Assert.IsNotNull(attr.Groups);
            Assert.HasCount(2, attr.Groups);
            Assert.AreEqual(PropertyGroup.Alarm, attr.Groups[0]);
            Assert.AreEqual(PropertyGroup.Status, attr.Groups[1]);
        }

        [TestMethod]
        public void DefaultAllFieldsToNull()
        {
            var attr = typeof(LogicBlockAttributeBareBlock).GetCustomAttribute<LogicBlockAttribute>()!;
            Assert.IsNull(attr.Name);
            Assert.IsNull(attr.Icon);
            Assert.IsNull(attr.Groups);
        }
    }
}
