using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Examples.Presentation.LogicBlocks;

namespace Vion.Examples.Presentation
{
    /// <summary>
    ///     DI registration for the declarative-presentation visual fixture.
    /// </summary>
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<PresentationDemo>();
        }
    }
}
