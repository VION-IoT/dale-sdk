using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     A block that throws from <see cref="LogicBlockBase.Starting" />. The actor middleware catches the
    ///     throw, so the block never reaches its <c>StartLogicBlockResponse</c> — the one shape that makes a
    ///     start acknowledgement never arrive, and the shape the host's start-health surface exists to report.
    ///     <para>
    ///         Used by <see cref="HostHealthShould" /> for both halves: the wall-clock backstop that keeps a
    ///         stepped host from hanging on the missing acknowledgement, and the failure the control surface
    ///         and <c>GET /api/control/status</c> report afterwards.
    ///     </para>
    /// </summary>
    [LogicBlock(Name = "Failing start")]
    public class FailingStartBlock : LogicBlockBase
    {
        /// <summary>The message the block throws with — what a health report must carry through to the reader.</summary>
        public const string FailureMessage = "this block refuses to start";

        [ServiceProperty(Title = "Never published")]
        public int NeverPublished { get; set; }

        public FailingStartBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
            throw new InvalidOperationException(FailureMessage);
        }
    }

    /// <summary>
    ///     A block whose constructor throws — the only way an actor creation can fail for a type that IS
    ///     registered. Used to pin which layer refuses first: introspection resolves the same type from the
    ///     root container before any actor exists, so the host never reaches the per-block creation.
    /// </summary>
    [LogicBlock(Name = "Failing constructor")]
    public class FailingConstructorBlock : LogicBlockBase
    {
        public const string FailureMessage = "this block refuses to be constructed";

        public FailingConstructorBlock(ILogger logger) : base(logger)
        {
            throw new InvalidOperationException(FailureMessage);
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     A block that throws from <see cref="LogicBlockBase.Ready" /> — the configuration phase of
    ///     <c>InitializeLogicBlock</c>. The send that carries it is fire-and-forget, so the throw is caught by
    ///     the actor middleware and the block still acknowledges start: the host comes up over a block whose
    ///     members never publish. The finding ledger's GATE row 66, and what the health surface reports.
    /// </summary>
    [LogicBlock(Name = "Failing configure")]
    public class FailingConfigureBlock : LogicBlockBase
    {
        public const string FailureMessage = "this block refuses to configure";

        [ServiceProperty(Title = "Never published")]
        public int NeverPublished { get; set; }

        public FailingConfigureBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            throw new InvalidOperationException(FailureMessage);
        }
    }
}
