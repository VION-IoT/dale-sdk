using System;
using Vion.Dale.Sdk.Utils;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    /// <summary>
    ///     Base interface for logic sender interfaces
    /// </summary>
    public interface ILogicSenderInterface
    {
        /// <summary>
        ///     Linked logic function interface IDs
        /// </summary>
        IReadOnlyCollection<InterfaceId> LinkedInterfaceIds { get; }

        /// <summary>
        ///     Type of the logic interface this sender interface is linked to
        /// </summary>
        Type LogicInterfaceType { get; }
    }
}