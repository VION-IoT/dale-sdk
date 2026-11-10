using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Everything one read returned: the telegrams in the order the meter sent them, and their records joined in
    ///     that order.
    /// </summary>
    /// <remarks>
    ///     A readout is incomplete when its last telegram announces more records than were read: the provider stopped
    ///     before the meter did. Values from the telegrams that were not read are then missing, never zero. Fewer
    ///     telegrams than the read asked for means the provider stopped; as many as asked for means the meter sends
    ///     more than the read asked for.
    /// </remarks>
    [PublicApi]
    public sealed class MBusReadout
    {
        internal MBusReadout(IReadOnlyList<MBusTelegram> telegrams)
        {
            Telegrams = telegrams;
            Records = telegrams.SelectMany(telegram => telegram.Records).ToList().AsReadOnly();
        }

        /// <summary>The header of the first telegram.</summary>
        public MBusHeader Header
        {
            get => Telegrams[0].Header;
        }

        /// <summary>The telegrams, in the order the meter sent them.</summary>
        public IReadOnlyList<MBusTelegram> Telegrams { get; }

        /// <summary>The records of every telegram, in telegram order.</summary>
        public IReadOnlyList<MBusRecord> Records { get; }

        /// <summary>Whether the last telegram announces no further records, so every record the meter sends was read.</summary>
        public bool IsComplete
        {
            get => !Telegrams[Telegrams.Count - 1].AnnouncesMoreRecords;
        }

        /// <summary>
        ///     Checks and decodes the frames one read returned from the meter at <paramref name="primaryAddress" />.
        /// </summary>
        /// <exception cref="MBusDecodeException">
        ///     There is no frame or more than <paramref name="maxTelegrams" />, a frame fails its checks or cannot be
        ///     decoded, a frame comes from another address, or the identification changes between frames.
        /// </exception>
        internal static MBusReadout Create(IReadOnlyList<byte[]> frames, int maxTelegrams, int primaryAddress)
        {
            if (frames.Count == 0)
            {
                throw new MBusDecodeException("The provider answered Ok with no telegram.");
            }

            if (frames.Count > maxTelegrams)
            {
                throw new MBusDecodeException($"The provider answered with {frames.Count} telegrams to a read of at most {maxTelegrams}.");
            }

            var telegrams = new List<MBusTelegram>(frames.Count);
            foreach (var frame in frames)
            {
                var telegram = MBusTelegram.Decode(frame);
                if (telegram.Address != primaryAddress)
                {
                    throw new MBusDecodeException($"Telegram {telegrams.Count + 1} comes from primary address {telegram.Address}, not from {primaryAddress}.");
                }

                // A second meter answering on the same primary address must not read as this meter's data.
                if (telegrams.Count > 0 && telegram.Header.Identification != telegrams[0].Header.Identification)
                {
                    throw new MBusDecodeException($"Telegram {telegrams.Count + 1} carries identification {telegram.Header.Identification}, " +
                                                  $"but telegram 1 carries {telegrams[0].Header.Identification}.");
                }

                telegrams.Add(telegram);
            }

            return new MBusReadout(telegrams.AsReadOnly());
        }
    }
}
