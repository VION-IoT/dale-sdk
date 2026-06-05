using Microsoft.Extensions.DependencyInjection;
using Proto.DependencyInjection;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.ProtoActor.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddProtoActorSystem(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton(serviceProvider => new Proto.ActorSystem().WithServiceProvider(serviceProvider));
            serviceCollection.AddSingleton<IActorSystem, ActorSystem>();
            serviceCollection.AddTransient(typeof(Actor<>));
        }
    }
}