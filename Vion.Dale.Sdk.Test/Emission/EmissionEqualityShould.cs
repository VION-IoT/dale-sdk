using System;
using System.Collections.Immutable;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    /// <summary>
    ///     The RFC 0004 value-equality floor's comparison, over every value shape a service element may take:
    ///     scalars, <c>string</c>, enums, flat <c>readonly record struct</c>s and <c>ImmutableArray&lt;T&gt;</c>
    ///     of those. The <c>ImmutableArray</c> cases are the reason this helper exists — a plain
    ///     <c>Equals</c> compares the underlying array by reference and reports every rebuilt table as changed.
    /// </summary>
    [TestClass]
    public class EmissionEqualityShould
    {
        [TestMethod]
        public void TreatARebuiltButIdenticalImmutableArrayAsUnchanged()
        {
            var emitted = ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Faulted));
            var rebuilt = ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Faulted));

            // The bug this closes: reference-distinct, content-identical.
            Assert.IsFalse(Equals(Box(emitted), Box(rebuilt)), "Equals is expected to compare the underlying array by reference.");
            Assert.IsTrue(EmissionEquality.AreEqual(Box(emitted), Box(rebuilt)));
        }

        [TestMethod]
        public void TreatASingleChangedFieldInOneRowAsChanged()
        {
            var emitted = ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Faulted));
            var changed = ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Ok));

            Assert.IsFalse(EmissionEquality.AreEqual(Box(emitted), Box(changed)));
        }

        [TestMethod]
        public void TreatADifferentRowCountAsChanged()
        {
            var emitted = ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Ok));
            var shorter = ImmutableArray.Create(new Row("a", 1.5d, Status.Ok));

            Assert.IsFalse(EmissionEquality.AreEqual(Box(emitted), Box(shorter)));
        }

        [TestMethod]
        public void TreatAReorderedImmutableArrayAsChanged()
        {
            // Row order is part of the value (it is the table's row order on a dashboard), so a permutation
            // is news even though the set of rows is identical.
            var emitted = ImmutableArray.Create(new Row("a", 1.5d, Status.Ok), new Row("b", 2.5d, Status.Ok));
            var swapped = ImmutableArray.Create(new Row("b", 2.5d, Status.Ok), new Row("a", 1.5d, Status.Ok));

            Assert.IsFalse(EmissionEquality.AreEqual(Box(emitted), Box(swapped)));
        }

        [TestMethod]
        public void CompareNestedImmutableArraysByContent()
        {
            var emitted = ImmutableArray.Create(ImmutableArray.Create(1, 2), ImmutableArray.Create(3));
            var rebuilt = ImmutableArray.Create(ImmutableArray.Create(1, 2), ImmutableArray.Create(3));

            Assert.IsTrue(EmissionEquality.AreEqual(Box(emitted), Box(rebuilt)));
            Assert.IsFalse(EmissionEquality.AreEqual(Box(emitted), Box(ImmutableArray.Create(ImmutableArray.Create(1, 2), ImmutableArray.Create(4)))));
        }

        [TestMethod]
        public void CompareDefaultImmutableArraysWithoutThrowing()
        {
            // A [ServiceProperty] typed ImmutableArray<T> with no initializer is `default` (DALE018 warns, but
            // the floor must not be the thing that throws).
            ImmutableArray<Row> uninitialised = default;

            Assert.IsTrue(EmissionEquality.AreEqual(Box(uninitialised), Box(default(ImmutableArray<Row>))));
            Assert.IsFalse(EmissionEquality.AreEqual(Box(uninitialised), Box(ImmutableArray<Row>.Empty)));
            Assert.IsFalse(EmissionEquality.AreEqual(Box(uninitialised), Box(ImmutableArray.Create(new Row("a", 1d, Status.Ok)))));
        }

        [TestMethod]
        public void TreatTwoEmptyImmutableArraysAsUnchanged()
        {
            Assert.IsTrue(EmissionEquality.AreEqual(Box(ImmutableArray<Row>.Empty), Box(ImmutableArray.Create<Row>())));
        }

        [TestMethod]
        public void KeepValueEqualityForTheScalarAndStructShapes()
        {
            Assert.IsTrue(EmissionEquality.AreEqual(1.5d, 1.5d));
            Assert.IsFalse(EmissionEquality.AreEqual(1.5d, 1.6d));
            Assert.IsTrue(EmissionEquality.AreEqual("x", "x"));
            Assert.IsFalse(EmissionEquality.AreEqual("x", "y"));
            Assert.IsTrue(EmissionEquality.AreEqual(Status.Faulted, Status.Faulted));
            Assert.IsFalse(EmissionEquality.AreEqual(Status.Faulted, Status.Ok));
            Assert.IsTrue(EmissionEquality.AreEqual(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            Assert.IsTrue(EmissionEquality.AreEqual(new Row("a", 1d, Status.Ok), new Row("a", 1d, Status.Ok)));
            Assert.IsFalse(EmissionEquality.AreEqual(new Row("a", 1d, Status.Ok), new Row("a", 1d, Status.Faulted)));
        }

        [TestMethod]
        public void TreatANullOnEitherSideAloneAsChanged()
        {
            Assert.IsTrue(EmissionEquality.AreEqual(null, null));
            Assert.IsFalse(EmissionEquality.AreEqual(Box(ImmutableArray.Create(new Row("a", 1d, Status.Ok))), null));
            Assert.IsFalse(EmissionEquality.AreEqual(null, Box(ImmutableArray.Create(new Row("a", 1d, Status.Ok)))));
            Assert.IsFalse(EmissionEquality.AreEqual(null, "x"));
            Assert.IsFalse(EmissionEquality.AreEqual("x", null));
        }

        /// <summary>
        ///     The floor receives values as <c>object?</c> (the binding getter's return), so every case is
        ///     compared boxed — which is exactly what defeats <c>ImmutableArray&lt;T&gt;</c>'s
        ///     <c>IEquatable</c>.
        /// </summary>
        private static object Box<T>(T value)
        {
            return value!;
        }

        private enum Status
        {
            Ok,

            Faulted,
        }

        private readonly record struct Row(string Name, double Power, Status Status);
    }
}