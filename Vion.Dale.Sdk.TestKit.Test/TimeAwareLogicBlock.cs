using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     A logic block that takes a <see cref="TimeProvider" /> via its constructor and exposes
    ///     <c>UtcNow</c> snapshots so tests can prove the virtual clock flowed through DI.
    /// </summary>
    public class TimeAwareLogicBlock : LogicBlockBase
    {
        private readonly TimeProvider _timeProvider;

        public TimeAwareLogicBlock(TimeProvider timeProvider, ILogger logger) : base(logger)
        {
            _timeProvider = timeProvider;
        }

        public DateTime SnapshotUtcNow()
        {
            return _timeProvider.GetUtcNow().UtcDateTime;
        }

        protected override void Ready()
        {
        }
    }
}