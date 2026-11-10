using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     The fixed header of a variable data telegram: who the meter is and the state it reports.
    /// </summary>
    [PublicApi]
    public sealed class MBusHeader
    {
        internal MBusHeader(string identification, string manufacturer, byte version, byte medium, byte accessNumber, MBusStatus status)
        {
            Identification = identification;
            Manufacturer = manufacturer;
            Version = version;
            Medium = medium;
            AccessNumber = accessNumber;
            Status = status;
        }

        /// <summary>The identification number, as its eight digits with leading zeros kept.</summary>
        public string Identification { get; }

        /// <summary>The manufacturer, as its three-letter code.</summary>
        public string Manufacturer { get; }

        /// <summary>The meter's version or generation, as the manufacturer defines it.</summary>
        public byte Version { get; }

        /// <summary>The medium (device type) code.</summary>
        public byte Medium { get; }

        /// <summary>The access number, which the meter increments with each answer.</summary>
        public byte AccessNumber { get; }

        /// <summary>The status byte.</summary>
        public MBusStatus Status { get; }
    }
}
