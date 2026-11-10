using System;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     The date and time data types of EN 13757-3 Annex A: G (a date, two bytes), F (date and time to the minute,
    ///     four bytes) and I (date and time to the second, six bytes). Type J, a time of day with no date, has no
    ///     <see cref="DateTime" /> to become and is left without a value.
    /// </summary>
    internal static class MBusTimePoint
    {
        public static DateTime? Decode(TimePointKind kind, int dataField, byte[] data)
        {
            return (kind, dataField) switch
            {
                (TimePointKind.Date, 0x02) => TypeG(data),
                (TimePointKind.DateAndTime, 0x04) => TypeF(data),
                (TimePointKind.DateAndTime, 0x06) => TypeI(data),
                _ => null,
            };
        }

        private static DateTime? TypeG(byte[] data)
        {
            return Create(2000 + YearOf(data[0], data[1]),
                          data[1] & 0x0F,
                          data[0] & 0x1F,
                          0,
                          0,
                          0);
        }

        private static DateTime? TypeF(byte[] data)
        {
            if ((data[0] & 0x80) != 0)
            {
                return null;
            }

            // Meters that predate the hundred-year bits count a two-digit year up to 80 as this century.
            var year = YearOf(data[2], data[3]);
            var hundredYear = (data[1] & 0x60) >> 5;
            if (hundredYear == 0 && year <= 80)
            {
                hundredYear = 1;
            }

            return Create(1900 + 100 * hundredYear + year,
                          data[3] & 0x0F,
                          data[2] & 0x1F,
                          data[1] & 0x1F,
                          data[0] & 0x3F,
                          0);
        }

        private static DateTime? TypeI(byte[] data)
        {
            if ((data[1] & 0x80) != 0)
            {
                return null;
            }

            return Create(2000 + YearOf(data[3], data[4]),
                          data[4] & 0x0F,
                          data[3] & 0x1F,
                          data[2] & 0x1F,
                          data[1] & 0x3F,
                          data[0] & 0x3F);
        }

        private static int YearOf(byte low, byte high)
        {
            return ((low & 0xE0) >> 5) | ((high & 0xF0) >> 1);
        }

        private static DateTime? Create(int year,
                                        int month,
                                        int day,
                                        int hour,
                                        int minute,
                                        int second)
        {
            if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
            {
                return null;
            }

            return new DateTime(year,
                                month,
                                day,
                                hour,
                                minute,
                                second,
                                DateTimeKind.Unspecified);
        }
    }
}