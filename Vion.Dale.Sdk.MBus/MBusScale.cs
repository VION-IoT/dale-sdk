using System;

namespace Vion.Dale.Sdk.MBus
{
    internal static class MBusScale
    {
        /// <summary>
        ///     <paramref name="value" /> times ten to <paramref name="exponent" />. A negative power divides rather than
        ///     multiplies: 795 / 10 is exactly the double nearest 79.5, while 795 * 0.1 is not.
        /// </summary>
        public static double Power(double value, int exponent)
        {
            return exponent < 0 ? value / Math.Pow(10, -exponent) : value * Math.Pow(10, exponent);
        }
    }
}