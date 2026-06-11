using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Thrown when a server-side register or bit access lies outside the declared extent of its area.
    /// </summary>
    [PublicApi]
    public class InvalidServerAddressException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="InvalidServerAddressException" /> class.
        /// </summary>
        /// <param name="area">The register area that was accessed.</param>
        /// <param name="startingAddress">The first address of the attempted access.</param>
        /// <param name="quantity">The number of registers or bits of the attempted access.</param>
        /// <param name="extent">The declared extent of the area (addresses 0 to extent - 1 are served).</param>
        public InvalidServerAddressException(ModbusServerArea area, ushort startingAddress, uint quantity, ushort extent)
            : base($"Access to {area} at address {startingAddress} (quantity {quantity}) lies outside the declared extent of {extent}.")
        {
        }
    }
}
