using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    /// <summary>
    ///     The comparison behind the dedup floor, over every value shape a service element may take:
    ///     scalars, <c>string</c>, enums, flat <c>readonly record struct</c>s and
    ///     <c>ImmutableArray&lt;T&gt;</c> of those. The array cases are why the helper exists — a plain
    ///     <c>Equals</c> compares the underlying array by reference and reports every rebuilt table as
    ///     changed. Values arrive boxed here because the binding getter returns <c>object?</c>, which is
    ///     exactly what defeats <c>ImmutableArray&lt;T&gt;</c>'s own <c>IEquatable</c>.
    /// </summary>
    [TestClass]
    public class EmissionEqualityShould
    {
        public enum Status
        {
            Ok,

            Faulted,
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.2")]
        public void TreatRebuiltButIdenticalTableAsUnchanged()
        {
            // Arrange
            var emitted = ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Faulted));
            var rebuilt = ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Faulted));

            // Act
            var unchanged = EmissionEquality.AreEqual(Box(emitted), Box(rebuilt));

            // Assert — and the reference comparison the floor must not use.
            Assert.IsTrue(unchanged);
            Assert.IsFalse(Equals(Box(emitted), Box(rebuilt)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.2")]
        public void TreatTwoEmptyTablesAsUnchanged()
        {
            // Arrange / Act / Assert
            Assert.IsTrue(EmissionEquality.AreEqual(Box(ImmutableArray<Row>.Empty), Box(ImmutableArray.Create<Row>())));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.2")]
        public void TreatUninitialisedTableAsEqualOnlyToAnother()
        {
            // Arrange — a service element typed ImmutableArray<T> with no initializer is `default`, and the
            // floor must compare it rather than throw.
            ImmutableArray<Row> uninitialised = default;

            // Act / Assert
            Assert.IsTrue(EmissionEquality.AreEqual(Box(uninitialised), Box(default(ImmutableArray<Row>))));
            Assert.IsFalse(EmissionEquality.AreEqual(Box(uninitialised), Box(ImmutableArray<Row>.Empty)));
            Assert.IsFalse(EmissionEquality.AreEqual(Box(uninitialised), Box(ImmutableArray.Create(new Row("a", 1d, Status.Ok)))));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.2")]
        public void TreatNestedTableAsChangedWhenInnerRowMoves()
        {
            // Arrange
            var emitted = ImmutableArray.Create(ImmutableArray.Create(1, 2), ImmutableArray.Create(3));

            // Act / Assert — the walk recurses, so the inner array is compared by content too.
            Assert.IsTrue(EmissionEquality.AreEqual(Box(emitted), Box(ImmutableArray.Create(ImmutableArray.Create(1, 2), ImmutableArray.Create(3)))));
            Assert.IsFalse(EmissionEquality.AreEqual(Box(emitted), Box(ImmutableArray.Create(ImmutableArray.Create(1, 2), ImmutableArray.Create(4)))));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.2")]
        [DynamicData(nameof(ChangedTables))]
        public void TreatChangedTableAsChanged(ImmutableArray<Row> emitted, ImmutableArray<Row> candidate)
        {
            // Arrange / Act
            var unchanged = EmissionEquality.AreEqual(Box(emitted), Box(candidate));

            // Assert
            Assert.IsFalse(unchanged);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.2")]
        [DynamicData(nameof(EqualScalarShapes))]
        public void TreatEqualScalarAndStructValuesAsUnchanged(object emitted, object candidate)
        {
            // Arrange / Act
            var unchanged = EmissionEquality.AreEqual(emitted, candidate);

            // Assert
            Assert.IsTrue(unchanged);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.2")]
        [DynamicData(nameof(DifferingScalarShapes))]
        public void TreatDifferingScalarAndStructValuesAsChanged(object emitted, object candidate)
        {
            // Arrange / Act
            var unchanged = EmissionEquality.AreEqual(emitted, candidate);

            // Assert
            Assert.IsFalse(unchanged);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.3")]
        public void TreatAbsentValueOnOneSideAsChanged()
        {
            // Arrange
            var table = Box(ImmutableArray.Create(new Row("a", 1d, Status.Ok)));

            // Act / Assert
            Assert.IsFalse(EmissionEquality.AreEqual(table, null));
            Assert.IsFalse(EmissionEquality.AreEqual(null, table));
            Assert.IsFalse(EmissionEquality.AreEqual(null, "x"));
            Assert.IsFalse(EmissionEquality.AreEqual("x", null));
            Assert.IsTrue(EmissionEquality.AreEqual(null, null));
        }

        public static IEnumerable<object[]> ChangedTables()
        {
            return new[]
                   {
                       // One field of one row moved.
                       new object[]
                       {
                           ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Faulted)),
                           ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Ok)),
                       },

                       // A row was dropped.
                       new object[]
                       {
                           ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Ok)),
                           ImmutableArray.Create(new Row("a", 1.5d, Status.Ok)),
                       },

                       // The same rows in a different order — row order is part of the table's value.
                       new object[]
                       {
                           ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Ok)),
                           ImmutableArray.Create(new Row("b", 2.5d, Status.Ok), new Row("a", 1.5d, Status.Ok)),
                       },
                   };
        }

        public static IEnumerable<object[]> EqualScalarShapes()
        {
            return new[]
                   {
                       new object[] { 1.5d, 1.5d },
                       new object[] { "x", "x" },
                       new object[] { Status.Faulted, Status.Faulted },
                       new object[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1) },
                       new object[] { new Row("a", 1d, Status.Ok), new Row("a", 1d, Status.Ok) },
                   };
        }

        public static IEnumerable<object[]> DifferingScalarShapes()
        {
            return new[]
                   {
                       new object[] { 1.5d, 1.6d },
                       new object[] { "x", "y" },
                       new object[] { Status.Faulted, Status.Ok },
                       new object[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) },
                       new object[] { new Row("a", 1d, Status.Ok), new Row("a", 1d, Status.Faulted) },
                   };
        }

        private static object Box<T>(T value)
        {
            return value!;
        }

        public readonly record struct Row(string Name, double Power, Status Status);
    }
}