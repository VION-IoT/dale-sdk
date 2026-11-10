namespace Vion.Dale.Sdk.MBus.Test.TestHelpers
{
    /// <summary>
    ///     Telegrams captured from real meters, as hexadecimal text from the start byte to the stop byte.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Carlo Gavazzi EM530</b>, primary address 1, 2400 Bd: one five-telegram read (access numbers D7h…DBh)
    ///         taken on a Beckhoff CX8200 with a KL6781 terminal on 2026-11-10. The frames were rebuilt from the
    ///         terminal's process image, and each checksum and stop byte was verified.
    ///     </para>
    ///     <para>
    ///         <b>Sontex Supercal 531</b> and <b>Elster TMP-A</b>: the libmbus test frames
    ///         <c>sontex_supercal_531_telegram1.hex</c> and <c>els_tmpa_telegramm1.hex</c>, copied under libmbus's
    ///         BSD 3-Clause licence, whose notice is <c>LIBMBUS-LICENSE.txt</c> beside this file.
    ///     </para>
    /// </remarks>
    internal static class MBusVectors
    {
        public const string Em530Telegram1 =
            "68 69 69 68 08 01 72 13 66 00 00 36 1C DD 02 D7 20 00 00 07 03 00 00 00 00 00 00 00 00 04 FB 82 75 00 00 00 00 04 2A 00 00 00 00 04 FB 97 72 00 00 00 00 04 FB B7 72 00 00 00 00 02 FD BA 73 E8 03 84 80 80 40 FD 48 20 03 00 00 04 FD 48 1B 03 00 00 84 40 FD 59 00 00 00 00 84 80 40 FD 59 00 00 00 00 84 C0 40 FD 59 00 00 00 00 1F D1 16";

        public const string Em530Telegram2 =
            "68 78 78 68 08 01 72 13 66 00 00 36 1C DD 02 D8 20 00 00 84 40 2A 00 00 00 00 84 80 40 2A 00 00 00 00 84 C0 40 2A 00 00 00 00 84 40 FB 97 72 00 00 00 00 84 80 40 FB 97 72 00 00 00 00 84 C0 40 FB 97 72 00 00 00 00 84 40 FB B7 72 00 00 00 00 84 80 40 FB B7 72 00 00 00 00 84 C0 40 FB B7 72 00 00 00 00 82 40 FD BA 73 E8 03 82 80 40 FD BA 73 E8 03 82 C0 40 FD BA 73 E8 03 1F 9B 16";

        public const string Em530Telegram3 =
            "68 75 75 68 08 01 72 13 66 00 00 36 1C DD 02 D9 20 00 00 84 C0 80 40 FD 48 B1 04 00 00 84 80 C0 40 FD 48 00 00 00 00 84 C0 C0 40 FD 48 B1 04 00 00 84 40 FD 48 52 09 00 00 84 80 40 FD 48 00 00 00 00 84 C0 40 FD 48 00 00 00 00 84 80 80 40 05 00 00 00 00 84 80 80 40 FB 82 75 00 00 00 00 84 C0 80 40 05 00 00 00 00 84 C0 80 40 FB 82 75 00 00 00 00 02 FB 2E F4 01 1F 16 16";

        public const string Em530Telegram4 =
            "68 67 67 68 08 01 72 13 66 00 00 36 1C DD 02 DA 20 00 00 84 40 05 00 00 00 00 84 80 40 05 00 00 00 00 84 C0 40 05 00 00 00 00 84 80 80 40 2A 00 00 00 00 84 C0 80 40 2A 00 00 00 00 84 80 80 40 FB B7 72 00 00 00 00 84 C0 80 40 FB B7 72 00 00 00 00 04 A6 74 00 00 00 00 84 40 A6 74 00 00 00 00 84 80 40 A6 74 2D 24 00 00 1F B0 16";

        public const string Em530Telegram5 =
            "68 2C 2C 68 08 01 72 13 66 00 00 36 1C DD 02 DB 20 00 00 84 80 C0 40 05 00 00 00 00 84 C0 C0 40 05 00 00 00 00 84 80 80 40 FD 59 E8 03 00 00 0F 86 16";

        public const string Supercal531Telegram1 =
            "68 51 51 68 08 01 72 24 06 42 08 EE 4D 0D 04 2C 30 00 00 04 0E 00 00 00 00 04 14 00 00 00 00 05 5B 00 00 00 00 05 5F 00 00 00 00 05 3E 00 00 00 00 05 2B 00 00 00 00 C4 00 0E 00 00 00 00 C4 00 14 00 00 00 00 C4 40 14 00 00 00 00 C4 80 40 14 00 00 00 00 1F 71 16";

        public const string ElsterTmpATelegram1 =
            "68 2C 2C 68 08 01 72 45 23 11 70 93 15 02 07 02 00 00 00 0C 13 67 45 23 01 04 6D 3A 0D E6 02 42 6C E1 01 4C 13 51 69 45 00 42 EC 7E 01 11 0F 00 61 16";

        public static readonly string[] Em530 = [Em530Telegram1, Em530Telegram2, Em530Telegram3, Em530Telegram4, Em530Telegram5];
    }
}