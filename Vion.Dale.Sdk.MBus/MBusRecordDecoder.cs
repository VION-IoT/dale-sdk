using System;
using System.Collections.Generic;
using System.Text;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Walks the data records of a variable data telegram (EN 13757-3): DIF and DIFEs, VIF and VIFEs, then the
    ///     data the DIF's data field sizes.
    /// </summary>
    internal static class MBusRecordDecoder
    {
        private const int MaxExtensions = 10;

        /// <summary>
        ///     Decodes the records in <paramref name="frame" /> from <paramref name="start" /> up to the checksum byte.
        /// </summary>
        /// <returns>The records, and whether the telegram ends on the marker that announces more records.</returns>
        public static (List<MBusRecord> Records, bool AnnouncesMoreRecords) Decode(byte[] frame, int start)
        {
            var records = new List<MBusRecord>();
            var end = frame.Length - 2;
            var index = start;
            while (index < end)
            {
                var dif = frame[index];
                switch (dif)
                {
                    case 0x2F:
                        index++;
                        continue;
                    case 0x0F:
                    case 0x1F:
                        // What follows is the manufacturer's, kept in the telegram's bytes and not decoded.
                        return (records, dif == 0x1F);
                }

                if ((dif & 0x0F) == 0x0F)
                {
                    throw new MBusDecodeException($"The data information field {dif:X2}h at offset {index} is a reserved special function.");
                }

                records.Add(DecodeRecord(frame, ref index, end));
            }

            return (records, false);
        }

        private static MBusRecord DecodeRecord(byte[] frame, ref int index, int end)
        {
            var recordStart = index;
            var dibStart = index;
            var dif = Next(frame, ref index, end, recordStart);
            var storage = (long)((dif >> 6) & 0x01);
            var tariff = 0;
            var subunit = 0;
            var extensions = 0;
            var last = dif;
            while ((last & 0x80) != 0)
            {
                if (++extensions > MaxExtensions)
                {
                    throw new MBusDecodeException($"The record at offset {recordStart} has more than {MaxExtensions} data information extensions.");
                }

                last = Next(frame, ref index, end, recordStart);
                var shift = extensions - 1;
                storage |= (long)(last & 0x0F) << (1 + 4 * shift);
                tariff |= ((last >> 4) & 0x03) << (2 * shift);
                subunit |= ((last >> 6) & 0x01) << shift;
            }

            var dib = Slice(frame, dibStart, index);

            var vibStart = index;
            var vif = Next(frame, ref index, end, recordStart);
            if ((vif & 0x7F) == 0x7C)
            {
                // A plain-text VIF: its length byte and characters sit between the VIF and any extensions.
                var textLength = Next(frame, ref index, end, recordStart);
                Skip(ref index, textLength, end, recordStart);
            }

            var extensionsStart = index - vibStart;
            extensions = 0;
            last = vif;
            while ((last & 0x80) != 0)
            {
                if (++extensions > MaxExtensions)
                {
                    throw new MBusDecodeException($"The record at offset {recordStart} has more than {MaxExtensions} value information extensions.");
                }

                last = Next(frame, ref index, end, recordStart);
            }

            var vib = Slice(frame, vibStart, index);
            if (vif is 0xFB or 0xFD && vib.Length < 2)
            {
                throw new MBusDecodeException($"The record at offset {recordStart} names an extended VIF table but carries no code from it.");
            }

            // For an extended table the first extension is the true VIF, so the extensions proper begin after it.
            var information = MBusValueInformation.Of(vib, vif is 0xFB or 0xFD ? 2 : extensionsStart);

            var dataStart = index;
            var dataField = dif & 0x0F;
            var length = dataField == 0x0D ? VariableLength(frame, index, end, recordStart) : FixedLength(dataField);
            Skip(ref index, length, end, recordStart);
            var data = Slice(frame, dataStart, index);

            double? number = null;
            DateTime? timestamp = null;
            string? text = null;
            if (!information.HasErrorCode)
            {
                if (information.TimePoint != TimePointKind.None)
                {
                    timestamp = MBusTimePoint.Decode(information.TimePoint, dataField, data);
                }
                else
                {
                    (number, text) = ReadValue(dataField, data);
                    if (number is { } raw && information.Quantity != null)
                    {
                        number = Scale(raw, information);
                    }
                }
            }

            return new MBusRecord(information.Quantity,
                                  (MBusFunction)((dif >> 4) & 0x03),
                                  storage,
                                  tariff,
                                  subunit,
                                  number,
                                  timestamp,
                                  text,
                                  dib,
                                  vib,
                                  data,
                                  information.HasMeaningChangingExtension);
        }

        /*
         * A negative power of ten divides rather than multiplies: 795 / 10 is exactly the double nearest 79.5, while
         * 795 * 0.1 is not. An additive correction is an offset in the VIF's own unit, so it is added before the VIF's
         * scale is applied.
         */
        private static double Scale(double raw, MBusValueInformation information)
        {
            if (information.AdditiveExponent is { } additive)
            {
                var corrected = Power(raw, information.CorrectionExponent) + Power(1, additive);
                return Power(corrected, information.VifExponent) * information.Factor;
            }

            return Power(raw, information.VifExponent + information.CorrectionExponent) * information.Factor;
        }

        private static double Power(double value, int exponent)
        {
            return exponent < 0 ? value / Math.Pow(10, -exponent) : value * Math.Pow(10, exponent);
        }

        private static (double? Number, string? Text) ReadValue(int dataField, byte[] data)
        {
            switch (dataField)
            {
                case 0x01:
                case 0x02:
                case 0x03:
                case 0x04:
                case 0x06:
                case 0x07:
                    return (SignedInteger(data, 0, data.Length), null);
                case 0x05:
                    return (BitConverter.ToSingle(LittleEndian(data), 0), null);
                case 0x09:
                case 0x0A:
                case 0x0B:
                case 0x0C:
                case 0x0E:
                    return (Bcd(data, 0, data.Length, false), null);
                case 0x0D:
                    return Variable(data);
                default:
                    return (null, null);
            }
        }

        private static (double? Number, string? Text) Variable(byte[] data)
        {
            var lvar = data[0];
            var length = data.Length - 1;
            switch (lvar)
            {
                case <= 0xBF:
                    // Characters travel last one first.
                    var characters = new StringBuilder(length);
                    for (var index = data.Length - 1; index >= 1; index--)
                    {
                        characters.Append((char)data[index]);
                    }

                    return (null, characters.ToString());
                case <= 0xCF:
                    return (Bcd(data, 1, length, false), null);
                case <= 0xDF:
                    return (Bcd(data, 1, length, true), null);
                default:
                    return (length <= 8 ? SignedInteger(data, 1, length) : null, null);
            }
        }

        private static double SignedInteger(byte[] data, int offset, int length)
        {
            if (length == 0)
            {
                return 0;
            }

            long value = 0;
            for (var index = length - 1; index >= 0; index--)
            {
                value = (value << 8) | data[offset + index];
            }

            // Sign-extend from the field's own width.
            var unused = 64 - 8 * length;
            return unused > 0 ? (value << unused) >> unused : value;
        }

        /// <summary>BCD digits, least significant byte first; a leading F nibble marks a negative number.</summary>
        private static double? Bcd(byte[] data, int offset, int length, bool negative)
        {
            double value = 0;
            for (var index = offset + length - 1; index >= offset; index--)
            {
                var high = data[index] >> 4;
                var low = data[index] & 0x0F;
                if (index == offset + length - 1 && high == 0x0F)
                {
                    negative = true;
                    high = 0;
                }

                if (high > 9 || low > 9)
                {
                    return null;
                }

                value = value * 100 + high * 10 + low;
            }

            return negative ? -value : value;
        }

        private static byte[] LittleEndian(byte[] data)
        {
            if (BitConverter.IsLittleEndian)
            {
                return data;
            }

            var reversed = (byte[])data.Clone();
            Array.Reverse(reversed);
            return reversed;
        }

        private static int FixedLength(int dataField)
        {
            return dataField switch
            {
                0x00 or 0x08 => 0,
                0x01 or 0x09 => 1,
                0x02 or 0x0A => 2,
                0x03 or 0x0B => 3,
                0x04 or 0x05 or 0x0C => 4,
                0x06 or 0x0E => 6,
                _ => 8,
            };
        }

        private static int VariableLength(byte[] frame, int index, int end, int recordStart)
        {
            if (index >= end)
            {
                throw Truncated(recordStart);
            }

            var lvar = frame[index];
            var payload = lvar switch
            {
                <= 0xBF => lvar,
                <= 0xCF => lvar - 0xC0,
                <= 0xDF => lvar - 0xD0,
                <= 0xEF => lvar - 0xE0,
                _ => throw new MBusDecodeException($"The record at offset {recordStart} carries the variable-length code {lvar:X2}h, whose length this decoder does not know."),
            };

            return 1 + payload;
        }

        private static byte Next(byte[] frame, ref int index, int end, int recordStart)
        {
            if (index >= end)
            {
                throw Truncated(recordStart);
            }

            return frame[index++];
        }

        private static void Skip(ref int index, int count, int end, int recordStart)
        {
            if (index + count > end)
            {
                throw Truncated(recordStart);
            }

            index += count;
        }

        private static byte[] Slice(byte[] frame, int start, int end)
        {
            var slice = new byte[end - start];
            Array.Copy(frame, start, slice, 0, slice.Length);
            return slice;
        }

        private static MBusDecodeException Truncated(int recordStart)
        {
            return new MBusDecodeException($"The record at offset {recordStart} runs past the end of the telegram's data.");
        }
    }
}
