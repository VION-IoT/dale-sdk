using System;
using Vion.Contracts.TypeRef;

namespace Vion.Dale.Sdk.Configuration.Services
{
    /// <summary>
    ///     A serializable view of a <see cref="ServiceBinding" /> for inter-actor communication —
    ///     the metadata document and target CLR type, without the Source/Getter/Setter delegates.
    ///     Carried by <see cref="Vion.Dale.Sdk.Messages.BindLogicBlockServices" /> so the runtime's
    ///     MQTT handlers can dispatch the FlatBuffer codec at the wire boundary using the per-binding
    ///     <c>TypeRef</c> schema without holding live binding references.
    /// </summary>
    public readonly record struct ServiceBindingInfo(PropertyMetadata Metadata, Type TargetClrType);
}
