using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test.TestHelpers
{
    /// <summary>
    ///     The three shapes <see cref="LogicBlockTestHelper" /> cannot construct, each with a different
    ///     reason a block author needs told apart. Shared by the two construction entry points' rows.
    /// </summary>
    public class NoLoggerConstructorBlock : LogicBlockBase
    {
        public NoLoggerConstructorBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <inheritdoc cref="NoLoggerConstructorBlock" />
    public abstract class AbstractBlock : LogicBlockBase
    {
        protected AbstractBlock(ILogger logger) : base(logger)
        {
        }
    }

    /// <inheritdoc cref="NoLoggerConstructorBlock" />
    public class ThrowingConstructorBlock : LogicBlockBase
    {
        public ThrowingConstructorBlock(ILogger logger) : base(logger)
        {
            throw new NotSupportedException("this block refuses to be constructed");
        }

        protected override void Ready()
        {
        }
    }
}