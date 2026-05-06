using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Examples.RichTypes.LogicBlocks;

namespace Vion.Examples.RichTypes
{
    /// <summary>
    ///     DI registration for the rich-types validation example.
    /// </summary>
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<RichBlock>();
        }
    }
}