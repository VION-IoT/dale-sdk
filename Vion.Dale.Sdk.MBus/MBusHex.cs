using System;
using System.Collections.Generic;
using System.Text;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Hexadecimal text as captures and datasheets write it: byte pairs, any case, separated by whitespace or not.
    /// </summary>
    internal static class MBusHex
    {
        public static byte[] Parse(string hex)
        {
            if (hex == null)
            {
                throw new ArgumentNullException(nameof(hex));
            }

            var bytes = new List<byte>(hex.Length / 2);
            var high = -1;
            foreach (var character in hex)
            {
                if (char.IsWhiteSpace(character))
                {
                    continue;
                }

                var nibble = NibbleOf(character);
                if (nibble < 0)
                {
                    throw new FormatException($"'{character}' is not a hexadecimal digit.");
                }

                if (high < 0)
                {
                    high = nibble;
                }
                else
                {
                    bytes.Add((byte)((high << 4) | nibble));
                    high = -1;
                }
            }

            if (high >= 0)
            {
                throw new FormatException("The hexadecimal text has an odd number of digits.");
            }

            return bytes.ToArray();
        }

        public static string Format(IReadOnlyList<byte> bytes)
        {
            var builder = new StringBuilder(bytes.Count * 3);
            for (var index = 0; index < bytes.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(bytes[index].ToString("X2"));
            }

            return builder.ToString();
        }

        private static int NibbleOf(char character)
        {
            return character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'A' and <= 'F' => character - 'A' + 10,
                >= 'a' and <= 'f' => character - 'a' + 10,
                _ => -1,
            };
        }
    }
}