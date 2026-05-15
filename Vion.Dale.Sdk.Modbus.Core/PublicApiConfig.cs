// Local PublicApi attribute shim for Vion.Dale.Sdk.Modbus.Core.
// This project intentionally has no dependency on Vion.Dale.Sdk to stay lightweight.
// The generate-api-reference.cjs script detects [PublicApi] by source scanning,
// so this local definition is sufficient for API reference generation.

using System;

namespace Vion.Dale.Sdk.Core
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Struct)]
    internal class PublicApiAttribute : Attribute
    {
    }
}