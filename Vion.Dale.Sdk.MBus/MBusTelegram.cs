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
        private const byte LongStart = 0x68;

        private const byte Stop = 0x16;

        private const byte VariableDataResponse = 0x72;

        private const int HeaderLength = 12;

        private const int DataStart = 7 + HeaderLength;

        private readonly byte[] _frame;

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

        private MBusTelegram(byte[] frame, MBusHeader header, List<MBusRecord> records, bool announcesMoreRecords)
        {
            _frame = frame;
            Raw = Array.AsReadOnly(frame);
            Header = header;
            Records = records.AsReadOnly();
            AnnouncesMoreRecords = announcesMoreRecords;
        }

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

        /// <summary>
        ///     One line per record, as <see cref="MBusReadout.Describe" /> prints a readout of this telegram alone: the
        ///     step from a captured telegram to the selectors a logic block declares.
        /// </summary>
        /// <returns>The description, in the invariant culture.</returns>
        public string Describe()
        {
            return new MBusReadout([this]).Describe();
        }

        /// <summary>The whole frame as hexadecimal text, two digits per byte separated by spaces.</summary>
        public override string ToString()
        {
            return MBusHex.Format(_frame);
        }

        /// <exception cref="MBusDecodeException">The frame cannot be decoded.</exception>
        internal static MBusTelegram Decode(byte[] frame)
        {
            Check(frame);
            var header = DecodeHeader(frame);
            var (records, announcesMoreRecords) = MBusRecordDecoder.Decode(frame, DataStart);

            return new MBusTelegram(frame, header, records, announcesMoreRecords);
        }

        /// <summary>
        ///     The long-frame checks of EN 13757-2 — start bytes, the repeated length, checksum and stop byte — and the
        ///     one control information this decoder reads, a variable data response with its long header.
        /// </summary>
        private static void Check(byte[]? frame)
        {
            if (frame == null)
            {
                throw new MBusDecodeException("A telegram is missing.");
            }

            if (frame.Length < 6 || frame[0] != LongStart || frame[3] != LongStart)
            {
                throw new MBusDecodeException("The frame does not start with the long-frame start bytes 68h L L 68h.");
            }

            if (frame[1] != frame[2])
            {
                throw new MBusDecodeException($"The frame's two length bytes differ ({frame[1]:X2}h, {frame[2]:X2}h).");
            }

            if (frame.Length != frame[1] + 6)
            {
                throw new MBusDecodeException($"The frame is {frame.Length} bytes long, but its length byte {frame[1]:X2}h makes it {frame[1] + 6}.");
            }

            var checksum = 0;
            for (var index = 4; index < frame.Length - 2; index++)
            {
                checksum += frame[index];
            }

            if ((byte)checksum != frame[frame.Length - 2])
            {
                throw new MBusDecodeException($"The frame's checksum is {frame[frame.Length - 2]:X2}h, but its bytes sum to {(byte)checksum:X2}h.");
            }

            if (frame[frame.Length - 1] != Stop)
            {
                throw new MBusDecodeException($"The frame ends on {frame[frame.Length - 1]:X2}h instead of the stop byte 16h.");
            }

            if (frame.Length < DataStart + 2)
            {
                throw new MBusDecodeException($"A frame of {frame.Length} bytes is too short for a variable data telegram.");
            }

            if (frame[6] != VariableDataResponse)
            {
                throw new MBusDecodeException($"The frame's control information is {frame[6]:X2}h; only a variable data response (72h) is decoded.");
            }
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

            return new MBusHeader(new string(identification),
                                  manufacturer,
                                  frame[13],
                                  frame[14],
                                  frame[15],
                                  new MBusStatus(frame[16]));
        }

        private static char HexDigit(int nibble)
        {
            return (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
        }
    }
}