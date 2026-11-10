using System;
using System.Linq;
using Vion.Dale.Sdk.MBus.Test.TestHelpers;

namespace Vion.Dale.Sdk.MBus.Test
{
    /// <summary>
    ///     Decoding one telegram into its header and records, driven by captured telegrams wherever a target meter
    ///     sends the coding, and by a synthetic telegram (<see cref="MBusFrames" />) only for codings none sends.
    /// </summary>
    [TestClass]
    public class MBusTelegramShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        [DataRow(MBusVectors.Em530Telegram1, "00006613", "GAV", (byte)0xDD, (byte)0x02, (byte)0xD7, (byte)0x20, DisplayName = "EM530")]
        [DataRow(MBusVectors.ElsterTmpATelegram1, "70112345", "ELS", (byte)0x02, (byte)0x07, (byte)0x02, (byte)0x00, DisplayName = "TMP-A")]
        [DataRow(MBusVectors.Supercal531Telegram1, "08420624", "SON", (byte)0x0D, (byte)0x04, (byte)0x2C, (byte)0x30, DisplayName = "Supercal 531")]
        public void DecodeHeader(string hex, string identification, string manufacturer, byte version, byte medium, byte accessNumber, byte status)
        {
            // Arrange

            // Act
            var header = MBusTelegram.Parse(hex).Header;

            // Assert
            Assert.AreEqual(identification, header.Identification);
            Assert.AreEqual(manufacturer, header.Manufacturer);
            Assert.AreEqual(version, header.Version);
            Assert.AreEqual(medium, header.Medium);
            Assert.AreEqual(accessNumber, header.AccessNumber);
            Assert.AreEqual(status, header.Status.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        public void DecodeRecordsInOrderKeepingTheirBytes()
        {
            // Arrange

            // Act
            var records = MBusTelegram.Parse(MBusVectors.Em530Telegram1).Records;

            // Assert
            Assert.AreEqual("07|03|00 00 00 00 00 00 00 00,04|FB 82 75|00 00 00 00,04|2A|00 00 00 00,04|FB 97 72|00 00 00 00,04|FB B7 72|00 00 00 00,02|FD BA 73|E8 03," +
                            "84 80 80 40|FD 48|20 03 00 00,04|FD 48|1B 03 00 00,84 40|FD 59|00 00 00 00,84 80 40|FD 59|00 00 00 00,84 C0 40|FD 59|00 00 00 00",
                            string.Join(",", records.Select(record => $"{Hex(record.Dib)}|{Hex(record.Vib)}|{Hex(record.Data)}")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        [DataRow(MBusVectors.Em530Telegram3, "84 C0 80 40", 0L, 0, 5, MBusFunction.Instantaneous, DisplayName = "EM530 subunit 5")]
        [DataRow(MBusVectors.Em530Telegram5, "84 80 C0 40", 0L, 0, 6, MBusFunction.Instantaneous, DisplayName = "EM530 subunit 6")]
        [DataRow(MBusVectors.Supercal531Telegram1, "C4 80 40", 1L, 0, 2, MBusFunction.Instantaneous, DisplayName = "Supercal storage 1 subunit 2")]
        [DataRow(MBusVectors.ElsterTmpATelegram1, "4C", 1L, 0, 0, MBusFunction.Instantaneous, DisplayName = "TMP-A storage 1")]
        public void DecodeDataInformationBlock(string hex, string dib, long storage, int tariff, int subunit, MBusFunction function)
        {
            // Arrange

            // Act
            var record = MBusTelegram.Parse(hex).Records.First(candidate => Hex(candidate.Dib) == dib);

            // Assert
            Assert.AreEqual(storage, record.Storage);
            Assert.AreEqual(tariff, record.Tariff);
            Assert.AreEqual(subunit, record.Subunit);
            Assert.AreEqual(function, record.Function);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        [DataRow("C2 84 01 03 00 00", 41L, 0, 0, MBusFunction.Instantaneous, DisplayName = "storage across two extensions")]
        [DataRow("84 10 03 00 00 00 00", 0L, 1, 0, MBusFunction.Instantaneous, DisplayName = "tariff 1")]
        [DataRow("84 A0 30 03 00 00 00 00", 0L, 14, 0, MBusFunction.Instantaneous, DisplayName = "tariff across two extensions")]
        [DataRow("14 03 00 00 00 00", 0L, 0, 0, MBusFunction.Maximum, DisplayName = "maximum")]
        [DataRow("24 03 00 00 00 00", 0L, 0, 0, MBusFunction.Minimum, DisplayName = "minimum")]
        [DataRow("34 03 00 00 00 00", 0L, 0, 0, MBusFunction.ValueDuringError, DisplayName = "value during error")]
        public void DecodeDataInformationCodingsNoCaptureSends(string recordHex, long storage, int tariff, int subunit, MBusFunction function)
        {
            // Arrange
            var hex = MBusFrames.Telegram(recordHex);

            // Act
            var record = MBusTelegram.Parse(hex).Records.Single();

            // Assert
            Assert.AreEqual(storage, record.Storage);
            Assert.AreEqual(tariff, record.Tariff);
            Assert.AreEqual(subunit, record.Subunit);
            Assert.AreEqual(function, record.Function);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.3")]
        [DataRow(MBusVectors.Em530Telegram1, "02 FD BA 73", MBusQuantity.Dimensionless, 1.0, DisplayName = "EM530 power factor")]
        [DataRow(MBusVectors.Em530Telegram1, "84 80 80 40 FD 48", MBusQuantity.Voltage, 80.0, DisplayName = "EM530 voltage subunit 4")]
        [DataRow(MBusVectors.Em530Telegram1, "04 FD 48", MBusQuantity.Voltage, 79.5, DisplayName = "EM530 voltage subunit 0")]
        [DataRow(MBusVectors.Em530Telegram3, "84 40 FD 48", MBusQuantity.Voltage, 238.6, DisplayName = "EM530 voltage subunit 1")]
        [DataRow(MBusVectors.Em530Telegram3, "84 C0 80 40 FD 48", MBusQuantity.Voltage, 120.1, DisplayName = "EM530 voltage subunit 5")]
        [DataRow(MBusVectors.Em530Telegram3, "02 FB 2E", MBusQuantity.Frequency, 50.0, DisplayName = "EM530 frequency")]
        [DataRow(MBusVectors.Em530Telegram4, "84 80 40 A6 74", MBusQuantity.OperatingTime, 92.61, DisplayName = "EM530 operating time")]
        [DataRow(MBusVectors.Em530Telegram5, "84 80 80 40 FD 59", MBusQuantity.Current, 1.0, DisplayName = "EM530 current subunit 4")]
        [DataRow(MBusVectors.ElsterTmpATelegram1, "0C 13", MBusQuantity.Volume, 1234.567, DisplayName = "TMP-A volume")]
        [DataRow(MBusVectors.ElsterTmpATelegram1, "4C 13", MBusQuantity.Volume, 456.951, DisplayName = "TMP-A due-day volume")]
        public void ScaleCapturedValueIntoReportedUnit(string hex, string code, MBusQuantity quantity, double expected)
        {
            // Arrange

            // Act
            var record = MBusTelegram.Parse(hex).Records.First(candidate => $"{Hex(candidate.Dib)} {Hex(candidate.Vib)}" == code);

            // Assert
            Assert.AreEqual(quantity, record.Quantity);
            Assert.AreEqual(expected, record.Number!.Value, 1e-9);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.3")]
        [DataRow("04 07 0C 00 00 00", MBusQuantity.Energy, 120000.0, DisplayName = "energy 10 kWh coding")]
        [DataRow("04 0E 24 00 00 00", MBusQuantity.Energy, 10000.0, DisplayName = "energy in MJ")]
        [DataRow("04 85 7D 0A 00 00 00", MBusQuantity.Energy, 1000000.0, DisplayName = "energy times a thousand")]
        [DataRow("04 83 79 0A 00 00 00", MBusQuantity.Energy, 10.01, DisplayName = "additive correction")]
        [DataRow("04 33 24 00 00 00", MBusQuantity.Power, 10.0, DisplayName = "power in kJ/h")]
        [DataRow("04 43 0A 00 00 00", MBusQuantity.VolumeFlow, 0.06, DisplayName = "volume flow per minute")]
        [DataRow("04 4B 0A 00 00 00", MBusQuantity.VolumeFlow, 0.036, DisplayName = "volume flow per second")]
        [DataRow("04 24 10 0E 00 00", MBusQuantity.OperatingTime, 1.0, DisplayName = "operating time in seconds")]
        [DataRow("04 27 02 00 00 00", MBusQuantity.OperatingTime, 48.0, DisplayName = "operating time in days")]
        [DataRow("02 61 E8 03", MBusQuantity.TemperatureDifference, 10.0, DisplayName = "temperature difference")]
        [DataRow("04 FB 82 75 0A 00 00 00", MBusQuantity.ReactiveEnergy, 1000.0, DisplayName = "reactive energy")]
        [DataRow("04 FB 97 72 0A 00 00 00", MBusQuantity.ReactivePower, 1.0, DisplayName = "reactive power")]
        [DataRow("04 FB B7 72 0A 00 00 00", MBusQuantity.ApparentPower, 1.0, DisplayName = "apparent power")]
        public void ScaleCodingNoCaptureCarriesIntoReportedUnit(string recordHex, MBusQuantity quantity, double expected)
        {
            // Arrange
            var hex = MBusFrames.Telegram(recordHex);

            // Act
            var record = MBusTelegram.Parse(hex).Records.Single();

            // Assert
            Assert.AreEqual(quantity, record.Quantity);
            Assert.AreEqual(expected, record.Number!.Value, 1e-9);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        [DataRow("01 FF 2C 07", 7.0, DisplayName = "8-bit integer")]
        [DataRow("02 FF 2C 18 FC", -1000.0, DisplayName = "negative 16-bit integer")]
        [DataRow("03 FF 2C 00 00 80", -8388608.0, DisplayName = "negative 24-bit integer")]
        [DataRow("06 FF 2C 01 00 00 00 00 80", -140737488355327.0, DisplayName = "negative 48-bit integer")]
        [DataRow("05 FF 2C 00 00 48 41", 12.5, DisplayName = "32-bit real")]
        [DataRow("09 FF 2C 42", 42.0, DisplayName = "2-digit BCD")]
        [DataRow("0A FF 2C 34 F2", -234.0, DisplayName = "negative 4-digit BCD")]
        [DataRow("0E FF 2C 56 34 12 90 78 56", 567890123456.0, DisplayName = "12-digit BCD")]
        [DataRow("0D FF 2C C2 34 12", 1234.0, DisplayName = "variable-length BCD")]
        [DataRow("0D FF 2C E2 FF FF", -1.0, DisplayName = "variable-length binary")]
        public void ReadRawNumberOfEachDataType(string recordHex, double expected)
        {
            // Arrange
            // VIF FFh is manufacturer-specific, so the record has no quantity and Number is the data's raw value.
            var hex = MBusFrames.Telegram(recordHex);

            // Act
            var record = MBusTelegram.Parse(hex).Records.Single();

            // Assert
            Assert.AreEqual(expected, record.Number!.Value, 1e-9);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        public void ReadVariableLengthTextInReadingOrder()
        {
            // Arrange
            var hex = MBusFrames.Telegram("0D FD 0C 03 33 32 31");

            // Act
            var record = MBusTelegram.Parse(hex).Records.Single();

            // Assert
            Assert.AreEqual("123", record.Text);
            Assert.IsNull(record.Number);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        [DataRow(MBusVectors.ElsterTmpATelegram1, "04 6D", "2007-02-06T13:58:00", DisplayName = "TMP-A type F")]
        [DataRow(MBusVectors.ElsterTmpATelegram1, "42 6C", "2007-01-01T00:00:00", DisplayName = "TMP-A type G")]
        [DataRow(MBusVectors.ElsterTmpATelegram1, "42 EC 7E", "2008-01-01T00:00:00", DisplayName = "TMP-A future value")]
        public void DecodeCapturedTimePoint(string hex, string code, string expected)
        {
            // Arrange

            // Act
            var record = MBusTelegram.Parse(hex).Records.First(candidate => $"{Hex(candidate.Dib)} {Hex(candidate.Vib)}" == code);

            // Assert
            Assert.AreEqual(MBusQuantity.DateTime, record.Quantity);
            Assert.AreEqual(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), record.Timestamp);
            Assert.IsNull(record.Number);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        [DataRow("06 6D 1E 3A 0D E6 02 00", "2007-02-06T13:58:30", DisplayName = "type I")]
        [DataRow("04 6D 3A 4D E6 02", "2107-02-06T13:58:00", DisplayName = "type F with hundred-year bits")]
        [DataRow("04 6D 3A 0D E6 BB", "1995-11-06T13:58:00", DisplayName = "type F two-digit year past 80")]
        [DataRow("04 6D BA 0D E6 02", null, DisplayName = "type F marked invalid")]
        [DataRow("06 6D 1E BA 0D E6 02 00", null, DisplayName = "type I marked invalid")]
        [DataRow("02 6C 00 00", null, DisplayName = "type G with no day")]
        public void DecodeTimePointCodingNoCaptureCarries(string recordHex, string? expected)
        {
            // Arrange
            var hex = MBusFrames.Telegram(recordHex);

            // Act
            var record = MBusTelegram.Parse(hex).Records.Single();

            // Assert
            Assert.AreEqual(expected == null ? null : DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), record.Timestamp);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.4")]
        [DataRow("04 FB 03 01 00 00 00", DisplayName = "extension code outside the table")]
        [DataRow("04 FF 01 01 00 00 00", DisplayName = "manufacturer-specific VIF")]
        [DataRow("04 1B 01 00 00 00", DisplayName = "primary VIF with no quantity")]
        [DataRow("04 7C 02 41 42 01 00 00 00", DisplayName = "plain-text VIF")]
        [DataRow("04 FC 02 41 42 2C 01 00 00 00", DisplayName = "plain-text VIF with an extension")]
        public void KeepRecordOutsideTableWithoutQuantity(string unknownRecordHex)
        {
            // Arrange
            var hex = MBusFrames.Telegram($"{unknownRecordHex} 02 FD BA 73 E8 03");

            // Act
            var records = MBusTelegram.Parse(hex).Records;

            // Assert
            Assert.HasCount(2, records);
            Assert.IsNull(records[0].Quantity);
            Assert.AreEqual(unknownRecordHex, $"{Hex(records[0].Dib)} {Hex(records[0].Vib)} {Hex(records[0].Data)}");
            Assert.AreEqual(1.0, records[0].Number);
            Assert.AreEqual(MBusQuantity.Dimensionless, records[1].Quantity);
            Assert.AreEqual(1.0, records[1].Number);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.5")]
        [DataRow("04 83 15 0A 00 00 00", MBusQuantity.Energy, DisplayName = "no data available")]
        [DataRow("04 FD C8 18 0A 00 00 00", MBusQuantity.Voltage, DisplayName = "data error after an extended VIF")]
        [DataRow("04 ED 16 3A 0D E6 02", MBusQuantity.DateTime, DisplayName = "data overflow on a time point")]
        public void KeepRecordWithErrorCodeWithoutValue(string recordHex, MBusQuantity quantity)
        {
            // Arrange
            var hex = MBusFrames.Telegram(recordHex);

            // Act
            var record = MBusTelegram.Parse(hex).Records.Single();

            // Assert
            Assert.AreEqual(quantity, record.Quantity);
            Assert.IsNull(record.Number);
            Assert.IsNull(record.Timestamp);
            Assert.IsNull(record.Text);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        public void SkipIdleFiller()
        {
            // Arrange
            var hex = MBusFrames.Telegram("2F 2F 02 FD BA 73 E8 03 2F");

            // Act
            var records = MBusTelegram.Parse(hex).Records;

            // Assert
            Assert.AreEqual("02", Hex(records.Single().Dib));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.2")]
        public void RenderFrameAsHexThatParsesBack()
        {
            // Arrange
            var telegram = MBusTelegram.Parse(MBusVectors.Em530Telegram5.ToLowerInvariant().Replace(" ", ""));

            // Act
            var rendered = telegram.ToString();

            // Assert
            Assert.AreEqual(MBusVectors.Em530Telegram5, rendered);
        }

        private static string Hex(System.Collections.Generic.IReadOnlyList<byte> bytes)
        {
            return MBusFrames.Hex(bytes.ToArray());
        }
    }
}
