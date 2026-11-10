using System;

namespace Vion.Dale.Sdk.MBus.Test
{
    [TestClass]
    public class MBusSelectorShould
    {
        [TestMethod]
        [DataRow(MBusQuantity.Energy,
                 0L,
                 0,
                 0,
                 MBusFunction.Instantaneous,
                 "MBusSelector.Of(MBusQuantity.Energy)")]
        [DataRow(MBusQuantity.Energy,
                 20L,
                 1,
                 6,
                 MBusFunction.Maximum,
                 "MBusSelector.Of(MBusQuantity.Energy, storage: 20, tariff: 1, subunit: 6, function: MBusFunction.Maximum)")]
        public void PrintAsCodeThatConstructsIt(MBusQuantity quantity,
                                                long storage,
                                                int tariff,
                                                int subunit,
                                                MBusFunction function,
                                                string expected)
        {
            // Arrange
            var selector = MBusSelector.Of(quantity, storage, tariff, subunit, function);

            // Act
            var printed = selector.ToString();

            // Assert
            Assert.AreEqual(expected, printed);
        }

        [TestMethod]
        public void PrintCodeInCanonicalHex()
        {
            // Arrange
            var selector = MBusSelector.ByCode("84c0804005");

            // Act
            var printed = selector.ToString();

            // Assert
            Assert.AreEqual("MBusSelector.ByCode(\"84 C0 80 40 05\")", printed);
        }

        [TestMethod]
        [DataRow(-1L, 0, 0, "storage")]
        [DataRow(0L, -1, 0, "tariff")]
        [DataRow(0L, 0, -1, "subunit")]
        public void RefuseNegativeQualifier(long storage, int tariff, int subunit, string parameter)
        {
            // Arrange

            // Act / Assert
            var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MBusSelector.Of(MBusQuantity.Energy, storage, tariff, subunit));
            Assert.AreEqual(parameter, exception.ParamName);
        }

        [TestMethod]
        public void RefuseEmptyCode()
        {
            // Arrange

            // Act / Assert
            Assert.ThrowsExactly<ArgumentException>(() => MBusSelector.ByCode(" "));
        }

        [TestMethod]
        public void EqualSelectorNamingSameRecord()
        {
            // Arrange
            var selector1 = MBusSelector.Of(MBusQuantity.Current, subunit: 1);
            var selector2 = MBusSelector.Of(MBusQuantity.Current, subunit: 1);
            var selector3 = MBusSelector.ByCode("84 40 FD 59");

            // Act
            var sameMeaning = selector1.Equals(selector2) && selector1.GetHashCode() == selector2.GetHashCode();
            var meaningAndCode = selector1.Equals(selector3);

            // Assert
            Assert.IsTrue(sameMeaning);
            Assert.IsFalse(meaningAndCode);
        }
    }
}