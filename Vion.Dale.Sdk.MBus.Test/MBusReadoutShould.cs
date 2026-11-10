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
    }
}
