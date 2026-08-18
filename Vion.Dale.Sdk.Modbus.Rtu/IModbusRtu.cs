using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Client;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Provides Modbus RTU read and write operations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The reads, writes and diagnostics are those of <see cref="IModbusClient" />. Declare the binding as
    ///         <c>IModbusRtu</c> — that is the type the runtime binds to a service provider contract — and then use it
    ///         through <see cref="IModbusClient" /> wherever the code should work for either transport.
    ///     </para>
    ///     <para>
    ///         Every instance shares one <see cref="ModbusRtuHandler" /> with every other Modbus RTU binding in the
    ///         runtime. Requests from all of them are published in the order the handler receives them, and they share
    ///         one pending-request limit of <see cref="ModbusRtuHandler.MaxPendingRequests" />: an outcome of
    ///         <c>Dropped</c> here may have been caused by another logic block. Expiry is checked by a sweep that runs
    ///         about once a second, so a timed-out operation can complete up to a second after its timeout.
    ///     </para>
    ///     <para>
    ///         Compared with Modbus TCP: there is no socket to report on, so there is no connection summary; the queue
    ///         is the shared handler's rather than this client's, so <c>Link.QueueDepth</c> is always zero; and the
    ///         default operation timeout is 5 seconds rather than 1. Like TCP, the timeout covers the wire only — it
    ///         starts when the handler publishes the request, and the hop before that is what <c>MaxQueuedAge</c>
    ///         bounds and what the receipt reports as <c>QueuedWait</c>.
    ///     </para>
    ///     <para>
    ///         The following exceptions may reach the error callback of any operation:
    ///         <see cref="InvalidUnitIdentifierException" /> (unit identifier below 0 or above 255),
    ///         <see cref="PendingRequestsLimitReachedException" /> (the shared pending-request limit was reached),
    ///         <see cref="ServiceProviderContractMappingNotFoundException" /> (the binding is not mapped to a service
    ///         provider contract), <see cref="RequestExpiredException" /> (the request aged past
    ///         <c>MaxQueuedAge</c> before it was published), <see cref="OperationTimeoutException" /> (the device did
    ///         not answer in time) and <see cref="ModbusException" /> (the device returned an error).
    ///     </para>
    ///     <para>
    ///         Operation-specific ones are <see cref="InvalidBitQuantityException" /> (fewer coils or discrete inputs
    ///         returned than requested), <see cref="InvalidCountException" /> (the resulting register quantity exceeds
    ///         65535) and <see cref="ModbusResponseAlignmentException" /> (the byte count does not match the registers
    ///         requested).
    ///     </para>
    /// </remarks>
    [PublicApi]
    [ServiceProviderContractType("ModbusRtu")]
    public interface IModbusRtu : IModbusClient
    {
    }
}