using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;

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

        protected override void Ready()
        {
        }
    }

    [TestClass]
    public sealed class InstantiationParameterGatingShould
    {
        [TestMethod]
        public void ApplyTheParameterValueThroughTheBuilderAndResolveGates()
        {
            var block = new TestKitGatedBlock();

            block.CreateTestContext().WithInstantiationParameter(lb => lb.PointCount, 2).Build();

            Assert.AreEqual(2, block.PointCount, "WithInstantiationParameter applies the value through the JSON decode path before Configure.");

            var serviceIds = BoundServiceIds(block);
            Assert.Contains("Point1", serviceIds);
            Assert.Contains("Point2", serviceIds);
            Assert.DoesNotContain("Point3", serviceIds); // gated out at PointCount = 2
        }

        [TestMethod]
        public void ResolveAgainstTheCSharpDefaultWhenNoParameterIsSupplied()
        {
            var block = new TestKitGatedBlock();

            block.CreateTestContext().Build();

            Assert.AreEqual(1, block.PointCount);
            var serviceIds = BoundServiceIds(block);
            Assert.Contains("Point1", serviceIds);
            Assert.DoesNotContain("Point2", serviceIds);
        }

        private static HashSet<string> BoundServiceIds(LogicBlockBase block)
        {
            var binder = (ServiceBinder)typeof(LogicBlockBase).GetField("_serviceBinder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(block)!;
            return binder.GetAllServicePropertyBindings().Keys.ToHashSet(StringComparer.Ordinal);
        }
    }
}