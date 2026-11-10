using System;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     A frame that fails its checks or cannot be decoded. It never leaves the package: the read path turns it into
    ///     a protocol error, and <see cref="MBusTelegram.Parse" /> into a <see cref="FormatException" />.
    /// </summary>
    internal sealed class MBusDecodeException : Exception
    {
        public MBusDecodeException(string message) : base(message)
        {
        }
    }
}
