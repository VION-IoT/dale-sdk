using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test.TestHelpers
{
    /// <summary>
    ///     Records the phases a <c>Build()</c> reaches, in the order it reaches them, so that the builder's
    ///     ordering is an observation rather than a reading of its source. Three of the five phases are
    ///     observable from inside a block: <c>Ready</c>, which runs only once initialization and the runtime
    ///     actor linking have both happened, the persistent restore, and <c>Starting</c>. The first two
    ///     phases have no hook of their own — that <c>Ready</c> ran at all is what shows they did.
    /// </summary>
    public class PhaseRecordingLogicBlock : LogicBlockBase
    {
        private int _restored;

        /// <summary>The phases that ran, in order.</summary>
        public List<string> Phases { get; } = [];

        [Persistent]
        [ServiceProperty]
        public int Restored
        {
            get => _restored;

            set
            {
                _restored = value;
                Phases.Add("Restore");
            }
        }

        public PhaseRecordingLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            Phases.Add("Ready");
        }

        protected override void Starting()
        {
            Phases.Add("Starting");
        }
    }

    /// <summary>
    ///     Two persistent members of different kinds — a real number and an enumeration — because the
    ///     builder stores an enumeration as its integer form and a restore that passed the enumeration
    ///     through would fail to bind rather than restore the wrong value.
    /// </summary>
    public class PersistentLogicBlock : LogicBlockBase
    {
        public enum OperatingMode
        {
            Automatic,

            Manual,
        }

        [Persistent]
        [ServiceProperty]
        public double MaxPower { get; set; }

        [Persistent]
        [ServiceProperty]
        public OperatingMode Mode { get; set; }

        public PersistentLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }
}