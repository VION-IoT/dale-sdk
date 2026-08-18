using Vion.Dale.Sdk.Core;

// Modbus RTU's actor messages (ReadModbusRtuRequest / ReadModbusRtuResponse and their write
// counterparts) live in the shared Vion.Dale.Sdk.Modbus.Rtu assembly but carry types declared here —
// ModbusReceipt and ModbusOutcome on the payload, and ByteOrder / WordOrder32 / WordOrder64 /
// TextEncoding / ModbusException across the IModbusRtu surface a logic block calls. Without this
// attribute every plugin loads its own copy of this assembly, those types get one identity per plugin,
// and cross-plugin message routing breaks in exactly the way DaleSharedAssembly exists to prevent.
[assembly: DaleSharedAssembly]