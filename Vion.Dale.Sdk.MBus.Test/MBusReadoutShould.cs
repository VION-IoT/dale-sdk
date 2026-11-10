using System;
using System.Linq;
using Vion.Dale.Sdk.MBus.Test.TestHelpers;

namespace Vion.Dale.Sdk.MBus.Test
{
    /// <summary>
    ///     A readout built from the frames of one read, as the contract builds it from a provider's answer. The read
    ///     path's own refusals — the error callback and its receipt — are <c>MBusShould</c>'s; this class pins what the
    ///     readout itself decides.
    /// </summary>
    [TestClass]
    public class MBusReadoutShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.7")]
        public void TakeHeaderFromFirstTelegram()
        {
            // Arrange
            var frames = MBusVectors.Em530.Select(MBusFrames.Bytes).ToList();

            // Act
            var readout = MBusReadout.Create(frames, 5, 1);

            // Assert
            Assert.AreEqual((byte)0xD7, readout.Header.AccessNumber);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.6")]
        [DataRow(5, true, DisplayName = "all five EM530 telegrams")]
        [DataRow(4, false, DisplayName = "the first four EM530 telegrams")]
        [DataRow(1, false, DisplayName = "the first EM530 telegram")]
        public void ReportCompleteOnlyWhenLastTelegramAnnouncesNoFurtherRecords(int count, bool expected)
        {
            // Arrange
            var frames = MBusVectors.Em530.Take(count).Select(MBusFrames.Bytes).ToList();

            // Act
            var readout = MBusReadout.Create(frames, 5, 1);

            // Assert
            Assert.AreEqual(expected, readout.IsComplete);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.6")]
        [DataRow(MBusVectors.ElsterTmpATelegram1, true, DisplayName = "TMP-A, ending on manufacturer data")]
        [DataRow(MBusVectors.Supercal531Telegram1, false, DisplayName = "Supercal 531, announcing more")]
        public void ReportCompletenessOfSingleTelegramRead(string hex, bool expected)
        {
            // Arrange
            var frames = new[] { MBusFrames.Bytes(hex) };

            // Act
            var readout = MBusReadout.Create(frames, 1, 1);

            // Assert
            Assert.AreEqual(expected, readout.IsComplete);
        }

        [TestMethod]
        public void JoinRecordsOfEveryTelegramInOrder()
        {
            // Arrange
            var frames = MBusVectors.Em530.Select(MBusFrames.Bytes).ToList();

            // Act
            var readout = MBusReadout.Create(frames, 5, 1);

            // Assert
            Assert.AreEqual("11,12,11,10,3", string.Join(",", readout.Telegrams.Select(telegram => telegram.Records.Count)));
            Assert.IsTrue(readout.Telegrams.SelectMany(telegram => telegram.Records).SequenceEqual(readout.Records));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.1")]
        [DataRow(MBusQuantity.Energy,
                 0L,
                 0,
                 0,
                 MBusFunction.Instantaneous,
                 "07|03",
                 DisplayName = "import energy, defaults")]
        [DataRow(MBusQuantity.Current,
                 0L,
                 0,
                 1,
                 MBusFunction.Instantaneous,
                 "84 40|FD 59",
                 DisplayName = "current L1, subunit 1")]
        [DataRow(MBusQuantity.Energy,
                 0L,
                 0,
                 6,
                 MBusFunction.Instantaneous,
                 "84 80 C0 40|05",
                 DisplayName = "import T1, subunit 6 in telegram 5")]
        [DataRow(MBusQuantity.Frequency,
                 0L,
                 0,
                 0,
                 MBusFunction.Instantaneous,
                 "02|FB 2E",
                 DisplayName = "frequency in telegram 3")]
        [DataRow(MBusQuantity.Voltage,
                 0L,
                 0,
                 5,
                 MBusFunction.Instantaneous,
                 "84 C0 80 40|FD 48",
                 DisplayName = "voltage L1-L2, subunit 5")]
        public void FindRecordByMeaning(MBusQuantity quantity,
                                        long storage,
                                        int tariff,
                                        int subunit,
                                        MBusFunction function,
                                        string expectedCode)
        {
            // Arrange
            var readout = Em530Readout();

            // Act
            var record = readout.Find(MBusSelector.Of(quantity, storage, tariff, subunit, function));

            // Assert
            Assert.IsNotNull(record);
            Assert.AreEqual(expectedCode, $"{Hex(record.Dib)}|{Hex(record.Vib)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.1")]
        [DataRow("84 10 03 01 00 00 00",
                 0L,
                 1,
                 0,
                 MBusFunction.Instantaneous,
                 DisplayName = "tariff 1")]
        [DataRow("C4 00 03 01 00 00 00",
                 1L,
                 0,
                 0,
                 MBusFunction.Instantaneous,
                 DisplayName = "storage 1")]
        [DataRow("14 03 01 00 00 00",
                 0L,
                 0,
                 0,
                 MBusFunction.Maximum,
                 DisplayName = "maximum")]
        public void TellRecordsApartByEveryQualifier(string qualifiedRecord, long storage, int tariff, int subunit, MBusFunction function)
        {
            // Arrange
            // The plain current value comes first, so a qualifier the match ignored would find it instead.
            var readout = Single(MBusFrames.Telegram($"04 03 00 00 00 00 {qualifiedRecord}"));

            // Act
            var record = readout.Find(MBusSelector.Of(MBusQuantity.Energy, storage, tariff, subunit, function));

            // Assert
            Assert.AreSame(readout.Records[1], record);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.1")]
        public void FindFirstMatchInTelegramOrder()
        {
            // Arrange
            // Two codings of the same current value: the 32-bit integer in kWh first, then BCD in Wh.
            var readout = Single(MBusFrames.Telegram("04 06 07 00 00 00 0C 03 00 00 00 00"));

            // Act
            var record = readout.Find(MBusSelector.Of(MBusQuantity.Energy));

            // Assert
            Assert.AreSame(readout.Records[0], record);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.1")]
        public void PassOverRecordWithMeaningChangingExtension()
        {
            // Arrange
            // VIFE 3Bh: the accumulation of positive contributions only, which is not the plain energy value.
            var readout = Single(MBusFrames.Telegram("04 83 3B 05 00 00 00"));

            // Act
            var byMeaning = readout.Find(MBusSelector.Of(MBusQuantity.Energy));
            var byCode = readout.Find(MBusSelector.ByCode("04 83 3B"));

            // Assert
            Assert.IsNull(byMeaning);
            Assert.AreSame(readout.Records[0], byCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.1")]
        public void FindRecordByCode()
        {
            // Arrange
            var readout = Em530Readout();

            // Act
            var record = readout.Find(MBusSelector.ByCode("84 C0 80 40 05"));

            // Assert
            Assert.AreSame(readout.Telegrams[2].Records[8], record);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.2")]
        public void ReturnNoValueWhenNothingMatches()
        {
            // Arrange
            var readout = Em530Readout();
            var selector = MBusSelector.Of(MBusQuantity.Volume);

            // Act
            var record = readout.Find(selector);
            var value = readout.Value(selector, MBusUnit.CubicMeter);

            // Assert
            Assert.IsNull(record);
            Assert.IsNull(value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.3")]
        [DataRow("84 80 40 A6 74", MBusUnit.Hour, 92.61, DisplayName = "operating time in h")]
        [DataRow("84 80 80 40 FD 48", MBusUnit.Volt, 80.0, DisplayName = "voltage in V")]
        [DataRow("84 80 80 40 FD 59", MBusUnit.Ampere, 1.0, DisplayName = "current in A")]
        [DataRow("02 FB 2E", MBusUnit.Hertz, 50.0, DisplayName = "frequency in Hz")]
        public void ReadCapturedValueInNamedUnit(string code, MBusUnit unit, double expected)
        {
            // Arrange
            var readout = Em530Readout();

            // Act
            var value = readout.Value(MBusSelector.ByCode(code), unit);

            // Assert
            Assert.AreEqual(expected, value!.Value, 1e-9);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.3")]
        [DataRow("04 03 D2 04 00 00", MBusQuantity.Energy, MBusUnit.WattHour, 1234.0, DisplayName = "Wh in Wh")]
        [DataRow("04 03 D2 04 00 00", MBusQuantity.Energy, MBusUnit.KilowattHour, 1.234, DisplayName = "Wh in kWh")]
        [DataRow("04 06 D2 04 00 00", MBusQuantity.Energy, MBusUnit.MegawattHour, 1.234, DisplayName = "kWh in MWh")]
        [DataRow("04 0E 24 00 00 00", MBusQuantity.Energy, MBusUnit.KilowattHour, 10.0, DisplayName = "MJ in kWh")]
        [DataRow("04 FB 82 75 0A 00 00 00", MBusQuantity.ReactiveEnergy, MBusUnit.KilovarHour, 1.0, DisplayName = "kvarh")]
        [DataRow("04 FB 82 75 0A 00 00 00", MBusQuantity.ReactiveEnergy, MBusUnit.VarHour, 1000.0, DisplayName = "varh")]
        [DataRow("04 13 D2 04 00 00", MBusQuantity.Volume, MBusUnit.Liter, 1234.0, DisplayName = "volume in L")]
        [DataRow("04 13 D2 04 00 00", MBusQuantity.Volume, MBusUnit.CubicMeter, 1.234, DisplayName = "volume in m³")]
        [DataRow("04 2A 0A 00 00 00", MBusQuantity.Power, MBusUnit.Watt, 1.0, DisplayName = "power in W")]
        [DataRow("04 2E 0A 00 00 00", MBusQuantity.Power, MBusUnit.Kilowatt, 10.0, DisplayName = "power in kW")]
        [DataRow("04 33 24 00 00 00", MBusQuantity.Power, MBusUnit.Watt, 10.0, DisplayName = "kJ/h in W")]
        [DataRow("04 FB 97 72 0A 00 00 00", MBusQuantity.ReactivePower, MBusUnit.Var, 1.0, DisplayName = "var")]
        [DataRow("04 FB 97 72 0A 00 00 00", MBusQuantity.ReactivePower, MBusUnit.Kilovar, 0.001, DisplayName = "kvar")]
        [DataRow("04 FB B7 72 0A 00 00 00", MBusQuantity.ApparentPower, MBusUnit.VoltAmpere, 1.0, DisplayName = "VA")]
        [DataRow("04 FB B7 72 0A 00 00 00", MBusQuantity.ApparentPower, MBusUnit.KilovoltAmpere, 0.001, DisplayName = "kVA")]
        [DataRow("04 3B D2 04 00 00", MBusQuantity.VolumeFlow, MBusUnit.LiterPerHour, 1234.0, DisplayName = "flow in L/h")]
        [DataRow("04 43 0A 00 00 00", MBusQuantity.VolumeFlow, MBusUnit.LiterPerHour, 60.0, DisplayName = "per-minute flow in L/h")]
        [DataRow("04 3E 0A 00 00 00", MBusQuantity.VolumeFlow, MBusUnit.CubicMeterPerHour, 10.0, DisplayName = "flow in m³/h")]
        [DataRow("02 5A D2 04", MBusQuantity.FlowTemperature, MBusUnit.DegreeCelsius, 123.4, DisplayName = "flow temperature")]
        [DataRow("02 5E D2 04", MBusQuantity.ReturnTemperature, MBusUnit.DegreeCelsius, 123.4, DisplayName = "return temperature")]
        [DataRow("02 62 D2 04", MBusQuantity.TemperatureDifference, MBusUnit.Kelvin, 123.4, DisplayName = "temperature difference")]
        public void ConvertValueIntoNamedUnit(string recordHex, MBusQuantity quantity, MBusUnit unit, double expected)
        {
            // Arrange
            var readout = Single(MBusFrames.Telegram(recordHex));

            // Act
            var value = readout.Value(MBusSelector.Of(quantity), unit);

            // Assert
            Assert.AreEqual(expected, value!.Value, 1e-9);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.3")]
        public void ThrowWhenUnitCannotExpressRecordFoundByCode()
        {
            // Arrange
            var readout = Em530Readout();
            var selector = MBusSelector.ByCode("84 80 80 40 FD 48");

            // Act / Assert
            var exception = Assert.ThrowsExactly<ArgumentException>(() => readout.Value(selector, MBusUnit.KilowattHour));
            Assert.AreEqual("MBusSelector.ByCode(\"84 80 80 40 FD 48\") found the record DIB 84 80 80 40 VIB FD 48 (Voltage), which cannot be expressed in KilowattHour. " +
                            "(Parameter 'unit')",
                            exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.3")]
        [DataRow(MBusQuantity.Voltage, MBusUnit.KilowattHour, DisplayName = "a unit of another quantity")]
        [DataRow(MBusQuantity.TemperatureDifference, MBusUnit.DegreeCelsius, DisplayName = "°C for a temperature difference")]
        [DataRow(MBusQuantity.FlowTemperature, MBusUnit.Kelvin, DisplayName = "K for an absolute temperature")]
        [DataRow(MBusQuantity.Dimensionless, MBusUnit.Hour, DisplayName = "any unit for a dimensionless value")]
        public void ThrowWhenUnitCannotExpressQuantityNamed(MBusQuantity quantity, MBusUnit unit)
        {
            // Arrange
            // The EM530 carries none of the temperatures, so the refusal cannot depend on a record being found.
            var readout = Em530Readout();
            var selector = MBusSelector.Of(quantity);

            // Act / Assert
            var exception = Assert.ThrowsExactly<ArgumentException>(() => readout.Value(selector, unit));
            StringAssert.StartsWith(exception.Message, $"{selector} names {quantity}, which cannot be expressed in {unit}.");
        }

        [TestMethod]
        public void ReturnNoValueForRecordWithoutValue()
        {
            // Arrange
            var readout = Single(MBusFrames.Telegram("04 83 15 0A 00 00 00"));

            // Act
            var value = readout.Value(MBusSelector.Of(MBusQuantity.Energy), MBusUnit.WattHour);

            // Assert
            Assert.IsNull(value);
        }

        [TestMethod]
        [DataRow(5, 1, DisplayName = "complete")]
        [DataRow(4, 0, DisplayName = "incomplete")]
        public void ReportMissOnlyOnCompleteReadout(int telegrams, int expectedMisses)
        {
            // Arrange
            var readout = MBusReadout.Create(MBusVectors.Em530.Take(telegrams).Select(MBusFrames.Bytes).ToList(), 5, 1);
            var misses = 0;
            readout.OnMiss = _ => misses++;

            // Act
            readout.Value(MBusSelector.Of(MBusQuantity.Volume), MBusUnit.CubicMeter);

            // Assert
            Assert.AreEqual(expectedMisses, misses);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.5")]
        [DataRow(MBusVectors.Em530Telegram1,
                 MBusVectors.Em530Telegram2,
                 MBusVectors.Em530Telegram3,
                 MBusVectors.Em530Telegram4,
                 MBusVectors.Em530Telegram5,
                 DisplayName = "EM530")]
        [DataRow(MBusVectors.ElsterTmpATelegram1, DisplayName = "TMP-A")]
        [DataRow(MBusVectors.Supercal531Telegram1, DisplayName = "Supercal 531")]
        public void GiveSelectorThatFindsEachRecord(params string[] telegrams)
        {
            // Arrange
            var readout = MBusReadout.Create(telegrams.Select(MBusFrames.Bytes).ToList(), telegrams.Length, 1);

            // Act
            var selectors = readout.Records.Select(readout.SelectorFor).ToList();

            // Assert
            var lost = readout.Records.Where((record, index) => selectors[index] == null || !ReferenceEquals(readout.Find(selectors[index]!), record)).ToList();
            Assert.IsEmpty(lost, $"{lost.Count} records have no selector that finds them.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.5")]
        [DataRow("04 06 07 00 00 00 0C 06 00 00 00 00", 1, "MBusSelector.ByCode(\"0C 06\")", DisplayName = "same meaning, later coding")]
        [DataRow("04 83 3B 05 00 00 00", 0, "MBusSelector.ByCode(\"04 83 3B\")", DisplayName = "meaning-changing extension")]
        [DataRow("04 FF 2C 05 00 00 00", 0, "MBusSelector.ByCode(\"04 FF 2C\")", DisplayName = "no quantity")]
        [DataRow("84 80 C0 40 05 05 00 00 00", 0, "MBusSelector.Of(MBusQuantity.Energy, subunit: 6)", DisplayName = "unambiguous meaning")]
        public void PreferSelectorByMeaningWhereUnambiguous(string recordsHex, int index, string expected)
        {
            // Arrange
            var readout = Single(MBusFrames.Telegram(recordsHex));

            // Act
            var selector = readout.SelectorFor(readout.Records[index]);

            // Assert
            Assert.AreEqual(expected, selector!.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.5")]
        public void GiveNoSelectorForShadowedRecord()
        {
            // Arrange
            // The same date/time record in two telegrams, the minute rolled over between them.
            var frames = new[]
                         {
                             MBusFrames.Bytes(MBusFrames.Telegram("04 6D 3A 0D E6 02 1F")),
                             MBusFrames.Bytes(MBusFrames.Telegram("04 6D 3B 0D E6 02")),
                         };
            var readout = MBusReadout.Create(frames, 2, 1);

            // Act
            var winner = readout.SelectorFor(readout.Records[0]);
            var shadowed = readout.SelectorFor(readout.Records[1]);

            // Assert
            Assert.AreEqual("MBusSelector.Of(MBusQuantity.DateTime)", winner!.ToString());
            Assert.IsNull(shadowed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.5")]
        public void DescribeOneLinePerRecordWithItsSelector()
        {
            // Arrange
            var readout = Em530Readout();

            // Act
            var lines = readout.Describe().Split('\n');

            // Assert
            Assert.HasCount(readout.Records.Count, lines);
            Assert.AreEqual("#1 | t1 | Energy | 0 Wh | storage 0, tariff 0, subunit 0, Instantaneous | DIB 07 | VIB 03 | MBusSelector.Of(MBusQuantity.Energy)", lines[0]);
            Assert.AreEqual("#8 | t1 | Voltage | 79.5 V | storage 0, tariff 0, subunit 0, Instantaneous | DIB 04 | VIB FD 48 | MBusSelector.Of(MBusQuantity.Voltage)", lines[7]);
            Assert.AreEqual("#44 | t4 | OperatingTime | 92.61 h | storage 0, tariff 0, subunit 2, Instantaneous | DIB 84 80 40 | VIB A6 74 | " +
                            "MBusSelector.Of(MBusQuantity.OperatingTime, subunit: 2)",
                            lines[43]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.5")]
        public void DescribeShadowedRecordByItsWinner()
        {
            // Arrange
            var frames = new[]
                         {
                             MBusFrames.Bytes(MBusFrames.Telegram("04 6D 3A 0D E6 02 1F")),
                             MBusFrames.Bytes(MBusFrames.Telegram("04 6D 3B 0D E6 02")),
                         };
            var readout = MBusReadout.Create(frames, 2, 1);

            // Act
            var lines = readout.Describe().Split('\n');

            // Assert
            Assert.AreEqual("#2 | t2 | DateTime | 2007-02-06 13:59:00 | storage 0, tariff 0, subunit 0, Instantaneous | DIB 04 | VIB 6D | shadowed by #1", lines[1]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-007.5")]
        public void DescribeTelegramAsReadoutOfItsOwn()
        {
            // Arrange
            var telegram = MBusTelegram.Parse(MBusVectors.ElsterTmpATelegram1);

            // Act
            var description = telegram.Describe();

            // Assert
            Assert.AreEqual("#1 | t1 | Volume | 1234.567 m³ | storage 0, tariff 0, subunit 0, Instantaneous | DIB 0C | VIB 13 | MBusSelector.Of(MBusQuantity.Volume)\n" +
                            "#2 | t1 | DateTime | 2007-02-06 13:58:00 | storage 0, tariff 0, subunit 0, Instantaneous | DIB 04 | VIB 6D | MBusSelector.Of(MBusQuantity.DateTime)\n" +
                            "#3 | t1 | DateTime | 2007-01-01 00:00:00 | storage 1, tariff 0, subunit 0, Instantaneous | DIB 42 | VIB 6C | MBusSelector.Of(MBusQuantity.DateTime, storage: 1)\n" +
                            "#4 | t1 | Volume | 456.951 m³ | storage 1, tariff 0, subunit 0, Instantaneous | DIB 4C | VIB 13 | MBusSelector.Of(MBusQuantity.Volume, storage: 1)\n" +
                            "#5 | t1 | DateTime | 2008-01-01 00:00:00 | storage 1, tariff 0, subunit 0, Instantaneous | DIB 42 | VIB EC 7E | MBusSelector.ByCode(\"42 EC 7E\")",
                            description);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-001.5")]
        public void DescribeInInvariantCulture()
        {
            // Arrange
            var readout = Em530Readout();
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            string description;

            try
            {
                // Act
                description = readout.Describe();
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = culture;
            }

            // Assert
            Assert.AreEqual("#44 | t4 | OperatingTime | 92.61 h | storage 0, tariff 0, subunit 2, Instantaneous | DIB 84 80 40 | VIB A6 74 | " +
                            "MBusSelector.Of(MBusQuantity.OperatingTime, subunit: 2)",
                            description.Split('\n')[43]);
        }

        [TestMethod]
        [DataRow(0, 5, 1, DisplayName = "no telegram")]
        [DataRow(5, 4, 1, DisplayName = "more telegrams than asked")]
        [DataRow(1, 5, 2, DisplayName = "another address")]
        public void RefuseReadThatDoesNotMatchRequest(int count, int maxTelegrams, int primaryAddress)
        {
            // Arrange
            var frames = MBusVectors.Em530.Take(count).Select(MBusFrames.Bytes).ToList();

            // Act / Assert
            Assert.ThrowsExactly<MBusDecodeException>(() => MBusReadout.Create(frames, maxTelegrams, primaryAddress));
        }

        [TestMethod]
        public void RefuseIdentificationThatChangesBetweenTelegrams()
        {
            // Arrange
            var frames = new[]
                         {
                             MBusFrames.Bytes(MBusFrames.Telegram("02 FD BA 73 E8 03 1F")),
                             MBusFrames.Bytes(MBusFrames.Telegram("02 FD BA 73 E8 03", identificationLsbFirst: "14 66 00 00")),
                         };

            // Act / Assert
            Assert.ThrowsExactly<MBusDecodeException>(() => MBusReadout.Create(frames, 2, 1));
        }

        [TestMethod]
        public void RefuseFrameThatCannotBeDecoded()
        {
            // Arrange
            var frames = new[] { MBusFrames.Bytes(MBusFrames.Telegram("04 FD 48 1B 03")) };

            // Act / Assert
            var exception = Assert.ThrowsExactly<MBusDecodeException>(() => MBusReadout.Create(frames, 1, 1));
            StringAssert.Contains(exception.Message, "runs past the end");
        }

        private static MBusReadout Em530Readout()
        {
            return MBusReadout.Create(MBusVectors.Em530.Select(MBusFrames.Bytes).ToList(), 5, 1);
        }

        private static MBusReadout Single(string hex)
        {
            return MBusReadout.Create([MBusFrames.Bytes(hex)], 1, 1);
        }

        private static string Hex(System.Collections.Generic.IReadOnlyList<byte> bytes)
        {
            return MBusFrames.Hex(bytes.ToArray());
        }
    }
}