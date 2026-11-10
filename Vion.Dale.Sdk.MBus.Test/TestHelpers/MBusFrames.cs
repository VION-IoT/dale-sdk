using System.Linq;
using System.Text;

namespace Vion.Dale.Sdk.MBus.Test.TestHelpers
{
    /// <summary>
    ///     Builds a valid variable data telegram around records a test writes by hand, for codings no captured meter
    ///     sends. The header is the EM530's, so a synthetic telegram reads as the same meter.
    /// </summary>
    internal static class MBusFrames
    {
        public static string Telegram(string recordsHex, byte address = 1, string identificationLsbFirst = "13 66 00 00", byte status = 0x00)
        {
            var body = Bytes($"08 {address:X2} 72 {identificationLsbFirst} 36 1C DD 02 00 {status:X2} 00 00 {recordsHex}");
            var checksum = (byte)body.Sum(b => b);
            var frame = new[] { (byte)0x68, (byte)body.Length, (byte)body.Length, (byte)0x68 }.Concat(body).Concat([checksum, (byte)0x16]).ToArray();

            return Hex(frame);
        }

        public static string Hex(byte[] bytes)
        {
            var builder = new StringBuilder();
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("X2")).Append(' ');
            }

            return builder.ToString().TrimEnd();
        }

        public static byte[] Bytes(string hex)
        {
            return hex.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Select(pair => System.Convert.ToByte(pair, 16)).ToArray();
        }
    }
}
