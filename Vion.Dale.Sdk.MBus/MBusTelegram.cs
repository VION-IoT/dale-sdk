using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     One variable data telegram (RSP_UD, CI 72h) as a meter sent it, decoded into its header and data records.
    /// </summary>
    /// <remarks>
    ///     <see cref="ToString" /> renders the whole frame as hexadecimal text that <see cref="Parse" /> reads back,
    ///     which is the form a test replays a capture in.
    /// </remarks>
    [PublicApi]
    public sealed class MBusTelegram
    {
        private const int HeaderLength = 12;

        private const int DataStart = 7 + HeaderLength;

        private readonly byte[] _frame;

        private MBusTelegram(byte[] frame, MBusHeader header, List<MBusRecord> records, bool announcesMoreRecords)
        {
            _frame = frame;
            Raw = Array.AsReadOnly(frame);
            Header = header;
            Records = records.AsReadOnly();
            AnnouncesMoreRecords = announcesMoreRecords;
        }

        /// <summary>The whole frame as received, from its start byte to its stop byte.</summary>
        public IReadOnlyList<byte> Raw { get; }

        /// <summary>The fixed header.</summary>
        public MBusHeader Header { get; }

        /// <summary>The data records, in the order the telegram carries them.</summary>
        public IReadOnlyList<MBusRecord> Records { get; }

        /// <summary>The address field: the primary address of the meter that answered.</summary>
        internal byte Address
        {
            get => _frame[5];
        }

        /// <summary>Whether the telegram ends on the marker that announces more records in a further telegram.</summary>
        internal bool AnnouncesMoreRecords { get; }

        /// <summary>
        ///     Decodes one telegram from hexadecimal text, such as a capture from a log or a datasheet.
        /// </summary>
        /// <param name="hex">The frame from its start byte to its stop byte; whitespace between the bytes is ignored.</param>
        /// <returns>The decoded telegram.</returns>
        /// <exception cref="FormatException">The text is not hexadecimal, or the frame cannot be decoded.</exception>
        public static MBusTelegram Parse(string hex)
        {
            var frame = MBusHex.Parse(hex);
            try
            {
                return Decode(frame);
            }
            catch (MBusDecodeException exception)
            {
                throw new FormatException(exception.Message, exception);
            }
        }

        /// <summary>The whole frame as hexadecimal text, two digits per byte separated by spaces.</summary>
        public override string ToString()
        {
            return MBusHex.Format(_frame);
        }

        /// <exception cref="MBusDecodeException">The frame cannot be decoded.</exception>
        internal static MBusTelegram Decode(byte[] frame)
        {
            if (frame.Length < DataStart + 2)
            {
                throw new MBusDecodeException($"A frame of {frame.Length} bytes is too short for a variable data telegram.");
            }

            var header = DecodeHeader(frame);
            var (records, announcesMoreRecords) = MBusRecordDecoder.Decode(frame, DataStart);

            return new MBusTelegram(frame, header, records, announcesMoreRecords);
        }

        private static MBusHeader DecodeHeader(byte[] frame)
        {
            // The identification is eight BCD digits, least significant byte first, kept as text so leading zeros stay.
            var identification = new char[8];
            for (var index = 0; index < 4; index++)
            {
                var digits = frame[7 + index];
                identification[7 - 2 * index] = HexDigit(digits & 0x0F);
                identification[6 - 2 * index] = HexDigit(digits >> 4);
            }

            var code = frame[11] | (frame[12] << 8);
            var manufacturer = new string([(char)(((code >> 10) & 0x1F) + 64), (char)(((code >> 5) & 0x1F) + 64), (char)((code & 0x1F) + 64)]);

            return new MBusHeader(new string(identification), manufacturer, frame[13], frame[14], frame[15], new MBusStatus(frame[16]));
        }

        private static char HexDigit(int nibble)
        {
            return (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
        }
    }
}
