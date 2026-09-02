using System;
using System.Globalization;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    // A custom value type and its deadband, both declared in THIS assembly — the shape the search must
    // resolve when the knobs are declared beside the block.
    public readonly record struct Meters(double Value);

    public sealed class MetersChangeThreshold : IChangeThreshold<Meters>
    {
        public bool Exceeds(in Meters lastEmitted, in Meters candidate, string threshold)
        {
            return Math.Abs(candidate.Value - lastEmitted.Value) >= double.Parse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    // A second custom type, used only by the sibling-assembly case so its cache entry cannot be filled by
    // the declaring-assembly case above.
    public readonly record struct Furlongs(double Value);

    public sealed class FurlongsChangeThreshold : IChangeThreshold<Furlongs>
    {
        public bool Exceeds(in Furlongs lastEmitted, in Furlongs candidate, string threshold)
        {
            return Math.Abs(candidate.Value - lastEmitted.Value) >= double.Parse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    // A third custom type whose only implementation cannot be constructed — the search must pass over it
    // rather than pick it and fail later.
    public readonly record struct Fathoms(double Value);

    public sealed class FathomsChangeThreshold : IChangeThreshold<Fathoms>
    {
        public double Scale { get; }

        public FathomsChangeThreshold(double scale)
        {
            Scale = scale;
        }

        public bool Exceeds(in Fathoms lastEmitted, in Fathoms candidate, string threshold)
        {
            return true;
        }
    }

    /// <summary>
    ///     How a member's deadband is found: the six built-ins first, then a search of the assembly that
    ///     declares the member, then of that assembly's SDK-referencing siblings in the same load context.
    ///     The result is kept for the process, so the search runs at most once per value type.
    /// </summary>
    [TestClass]
    public class ChangeThresholdRegistryShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-008.1")]
        [DataRow(typeof(double))]
        [DataRow(typeof(float))]
        [DataRow(typeof(decimal))]
        [DataRow(typeof(int))]
        [DataRow(typeof(long))]
        [DataRow(typeof(TimeSpan))]
        public void ResolveBuiltInDeadbandForValueType(Type valueType)
        {
            // Arrange / Act
            var resolved = ChangeThresholdRegistry.TryResolve(valueType, null, out var adapter);

            // Assert
            Assert.IsTrue(resolved);
            Assert.IsNotNull(adapter);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-009.1")]
        public void ResolveDeadbandDeclaredInSearchedAssembly()
        {
            // Arrange / Act
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(Meters), typeof(MetersChangeThreshold).Assembly, out var adapter);

            // Assert
            Assert.IsTrue(resolved);
            Assert.IsTrue(adapter.Exceeds(new Meters(10), new Meters(13), "2"));
            Assert.IsFalse(adapter.Exceeds(new Meters(10), new Meters(11), "2"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-009.2")]
        public void ResolveDeadbandDeclaredInSiblingAssembly()
        {
            // Arrange — search the SDK, which declares no threshold for this type; the implementation lives
            // in this test assembly, a sibling loaded in the same context. That is the foundation-library
            // shape: the deadband ships in one assembly, the knob is declared in another.
            var searched = typeof(ChangeThresholdRegistry).Assembly;

            // Act
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(Furlongs), searched, out var adapter);

            // Assert
            Assert.IsTrue(resolved);
            Assert.IsTrue(adapter.Exceeds(new Furlongs(10), new Furlongs(13), "2"));
            Assert.IsFalse(adapter.Exceeds(new Furlongs(10), new Furlongs(11), "2"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-009.1")]
        public void PassOverImplementationItCannotConstruct()
        {
            // Arrange / Act — FathomsChangeThreshold is the only implementation and takes a constructor
            // argument, so nothing usable is found.
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(Fathoms), typeof(FathomsChangeThreshold).Assembly, out var adapter);

            // Assert
            Assert.IsFalse(resolved);
            Assert.IsNull(adapter);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-009.1")]
        public void ResolveNothingForValueTypeWithNoDeadband()
        {
            // Arrange / Act
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(Guid), typeof(MetersChangeThreshold).Assembly, out var adapter);

            // Assert
            Assert.IsFalse(resolved);
            Assert.IsNull(adapter);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-009.1")]
        public void ResolveNothingWithoutAssemblyToSearch()
        {
            // Arrange / Act
            var resolved = ChangeThresholdRegistry.TryResolve(typeof(string), null, out var adapter);

            // Assert
            Assert.IsFalse(resolved);
            Assert.IsNull(adapter);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-009.3")]
        public void KeepResolvedDeadbandKeyedByValueType()
        {
            // Arrange — the first resolution runs the search and keeps its result.
            ChangeThresholdRegistry.TryResolve(typeof(Meters), typeof(MetersChangeThreshold).Assembly, out var first);

            // Act — the same value type, reached with nothing to search and then from a different
            // assembly. The key is the type, so neither call runs the search again.
            var withoutSearch = ChangeThresholdRegistry.TryResolve(typeof(Meters), null, out var second);
            var fromElsewhere = ChangeThresholdRegistry.TryResolve(typeof(Meters), typeof(ChangeThresholdRegistry).Assembly, out var third);

            // Assert — one deadband for the type, so two members of it share one wherever they are declared.
            Assert.IsTrue(withoutSearch);
            Assert.IsTrue(fromElsewhere);
            Assert.AreSame(first, second);
            Assert.AreSame(first, third);
        }
    }
}