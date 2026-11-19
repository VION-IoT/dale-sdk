using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Modbus.Core
{
    /// <summary>
    ///     The shared-handler probe for <c>Vion.Dale.Sdk.Modbus.Core</c>, a shared assembly with no handler actor
    ///     and no <c>IConfigureServices</c>: a concrete handler actor that leaves it with one and no registration,
    ///     which DALE049 must reject. Linked into that project only under
    ///     <c>-p:DaleSharedHandlerAnalyzerWiringProbe=true</c>, where the build MUST fail with the diagnostic. It
    ///     is nobody's source file otherwise. <c>AnalyzerWiringShould</c> runs it.
    /// </summary>
    internal sealed class SharedHandlerWiringProbe : IServiceProviderHandlerActor
    {
        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            return Task.CompletedTask;
        }
    }
}
