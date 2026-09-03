using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class AttributeInheritanceShould
    {
        [TestMethod]
        public void DerivedServicePropertyAttributeIsFoundByBaseType()
        {
            var prop = typeof(Subject).GetProperty(nameof(Subject.TestProperty))!;
            var attr = prop.GetCustomAttribute<ServicePropertyAttribute>();
            Assert.IsNotNull(attr);
            Assert.AreEqual("kW", attr.Unit);
            Assert.AreEqual(0.0, attr.Minimum);
        }

        [TestMethod]
        public void DerivedServiceMeasuringPointAttributeIsFoundByBaseType()
        {
            var prop = typeof(Subject).GetProperty(nameof(Subject.TestMeasuringPoint))!;
            var attr = prop.GetCustomAttribute<ServiceMeasuringPointAttribute>();
            Assert.IsNotNull(attr);
            Assert.AreEqual("kW", attr.Unit);
            Assert.AreEqual(0.0, attr.Minimum);
        }

        [TestMethod]
        public void DerivedStructFieldAttributeIsFoundByBaseType()
        {
            // The attribute is read off a struct's positional constructor parameter, which is the only
            // place either introspection reader looks for it.
            var parameter = typeof(SubjectStruct).GetConstructors()[0].GetParameters()[0];
            var attr = parameter.GetCustomAttribute<StructFieldAttribute>();
            Assert.IsNotNull(attr);
            Assert.AreEqual("V", attr.Unit);
        }

        public class TestKilowatts : ServicePropertyAttribute
        {
            public TestKilowatts()
            {
                Unit = "kW";
                Minimum = 0;
            }
        }

        public class TestKilowattsMeter : ServiceMeasuringPointAttribute
        {
            public TestKilowattsMeter()
            {
                Unit = "kW";
                Minimum = 0;
            }
        }

        public class TestStructField : StructFieldAttribute
        {
            public TestStructField()
            {
                Unit = "V";
            }
        }

        public class Subject
        {
            [TestKilowatts]
            public double TestProperty { get; set; }

            [TestKilowattsMeter]
            public double TestMeasuringPoint { get; private set; }
        }

        public readonly record struct SubjectStruct([TestStructField] double Volts);
    }
}