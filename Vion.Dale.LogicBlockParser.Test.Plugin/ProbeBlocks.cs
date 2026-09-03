using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Dale.ParserProbe
{
    // The plugin Vion.Dale.LogicBlockParser.Test runs the parser over. It is a real netstandard2.1
    // logic-block library — the only shape the parser's PluginLoadContext accepts — and every block below
    // is one case the parser's own behavior turns on: an ordinary block, a nested type name, and a
    // development-only binding. The unregistered-type case needs its own assembly, because a plugin
    // carrying one fails the whole run: Vion.Dale.LogicBlockParser.Test.UnregisteredPlugin.

    /// <summary>Registers every block in this assembly, so an ordinary run succeeds.</summary>
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<PlainBlock>();
            services.AddTransient<Grouping.NestedBlock>();
            services.AddTransient<DevelopmentOnlyBlock>();
        }
    }

    /// <summary>An ordinary block: one service, one property, one measuring point.</summary>
    [LogicBlock(Name = "Plain", Icon = "probe-line")]
    public class PlainBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Setpoint", Unit = "kW", Minimum = 0, Maximum = 100)]
        public double Setpoint { get; set; }

        [ServiceMeasuringPoint(Title = "Reading", Unit = "kW")]
        public double Reading { get; private set; }

        public PlainBlock(ILogger<PlainBlock> logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>A block nested in a static class, so its identity carries the CLR nesting separator.</summary>
    public static class Grouping
    {
        [LogicBlock(Name = "Nested")]
        public class NestedBlock : LogicBlockBase
        {
            [ServiceProperty(Title = "Value")]
            public int Value { get; set; }

            public NestedBlock(ILogger<NestedBlock> logger) : base(logger)
            {
            }

            protected override void Ready()
            {
            }
        }
    }

    /// <summary>Binds a provider face, so the development-only filter drops it.</summary>
    [LogicBlock(Name = "Bench")]
    public class DevelopmentOnlyBlock : LogicBlockBase
    {
        [ServiceProviderContractBinding(DefaultName = "Bench face")]
        public IDigitalOutputProvider Face { get; private set; } = null!;

        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        public DevelopmentOnlyBlock(ILogger<DevelopmentOnlyBlock> logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }
}