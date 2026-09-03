using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.ParserProbe.Unregistered
{
    // A plugin whose service registration omits one of its own concrete logic blocks. It lives in its own
    // assembly because that omission fails the whole introspection run, which is exactly the behavior
    // Vion.Dale.LogicBlockParser.Test asserts — every other parser case needs a plugin that succeeds.

    /// <summary>Registers <see cref="RegisteredBlock" /> and deliberately not <see cref="ForgottenBlock" />.</summary>
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<RegisteredBlock>();
        }
    }

    /// <summary>The block the registration above does declare.</summary>
    [LogicBlock(Name = "Registered")]
    public class RegisteredBlock : LogicBlockBase
    {
        public RegisteredBlock(ILogger<RegisteredBlock> logger) : base(logger)
        {
        }

        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        protected override void Ready()
        {
        }
    }

    /// <summary>The block the registration above forgets — a concrete type the parser cannot instantiate.</summary>
    [LogicBlock(Name = "Forgotten")]
    public class ForgottenBlock : LogicBlockBase
    {
        public ForgottenBlock(ILogger<ForgottenBlock> logger) : base(logger)
        {
        }

        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        protected override void Ready()
        {
        }
    }
}
