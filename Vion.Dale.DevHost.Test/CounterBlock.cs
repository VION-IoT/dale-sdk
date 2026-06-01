using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>Minimal single-service logic block for headless control-surface tests.</summary>
    [LogicBlock(Name = "Counter")]
    public class CounterBlock : LogicBlockBase
    {
        private int _counter;

        [ServiceProperty(Title = "Counter")]
        public int Counter
        {
            get => _counter;

            set
            {
                _counter = value;
                // A computed read-only metric — lets tests assert a "calculation" via a measuring point.
                CounterDoubled = value * 2;
            }
        }

        [ServiceMeasuringPoint(Title = "Counter doubled")]
        public int CounterDoubled { get; private set; }

        public CounterBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>DI registration discovered by DevHostBuilder.WithDi&lt;TestDependencyInjection&gt;().</summary>
    public class TestDependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<CounterBlock>();
        }
    }
}
