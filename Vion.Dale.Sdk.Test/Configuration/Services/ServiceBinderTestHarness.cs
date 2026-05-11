using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Configuration.Services
{
    /// <summary>
    ///     Creates a <see cref="ServiceBinder" /> that has been fully populated by the declarative binder for a given
    ///     <typeparamref name="T" /> logic block. The simplest correct approach: call
    ///     <see cref="DeclarativeServiceBinder.BindServicesFromAttributes" /> directly — it is <c>public static</c> and does
    ///     not require the full <c>LogicBlockConfigurationBuilder</c> machinery.
    /// </summary>
    internal static class ServiceBinderTestHarness
    {
        public static (ServiceBinder Binder, T Block) Bind<T>()
            where T : LogicBlockBase, new()
        {
            var block = new T();
            var binder = new ServiceBinder();
            DeclarativeServiceBinder.BindServicesFromAttributes(block, binder);
            return (binder, block);
        }
    }
}