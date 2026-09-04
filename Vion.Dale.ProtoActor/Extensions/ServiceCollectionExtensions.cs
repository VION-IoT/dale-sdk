using Microsoft.Extensions.DependencyInjection;
using Proto.DependencyInjection;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.ProtoActor.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        ///     Registers the actor system a host spawns logic blocks onto.
        /// </summary>
        /// <remarks>
        ///     The pipeline reads six things from the container and requires none of them: a clock, a message
        ///     observer, an actor-activity monitor, a delayed-send gate, a virtual schedule and a vitals
        ///     collector. Absent, each is simply not used — except the clock, which falls back to the real one,
        ///     so an actor system composed without <c>AddDaleSdk</c> runs every timeout and every measurement on
        ///     wall time and cannot be stepped. <c>AddDaleSdk</c> is what supplies the clock and the vitals core;
        ///     call it as well, and call it once — a second call registers the vitals core as a second observer
        ///     and every message is then counted twice.
        /// </remarks>
        public static void AddProtoActorSystem(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton(serviceProvider => new Proto.ActorSystem().WithServiceProvider(serviceProvider));
            serviceCollection.AddSingleton<IActorSystem, ActorSystem>();
            serviceCollection.AddTransient(typeof(Actor<>));
        }
    }
}