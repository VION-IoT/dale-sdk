using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;

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
        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        public RegisteredBlock(ILogger<RegisteredBlock> logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     The block the registration above forgets — a concrete type the parser cannot instantiate. It also
    ///     binds a provider face, so the development-only filter would leave it out of the document: the
    ///     filter decides what the document carries and never what the run checks, and this block is how that
    ///     is asserted.
    /// </summary>
    [LogicBlock(Name = "Forgotten")]
    public class ForgottenBlock : LogicBlockBase
    {
        [ServiceProviderContractBinding(DefaultName = "Bench face")]
        public IDigitalOutputProvider Face { get; private set; } = null!;

        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        public ForgottenBlock(ILogger<ForgottenBlock> logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     An abstract logic block, deliberately unregistered. Abstract types are not introspected, so its
    ///     absence from the registration above must not fail the run — the control for
    ///     <see cref="ForgottenBlock" />.
    /// </summary>
    public abstract class AbstractBaseBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        protected AbstractBaseBlock(ILogger logger) : base(logger)
        {
        }
    }
}