using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus.TestKit.Test
{
    public class SampleLogicBlock : LogicBlockBase
    {
        public const int PrimaryAddress = 1;

        private static readonly MBusSelector FrequencySel = MBusSelector.Of(MBusQuantity.Frequency);

        [ServiceProviderContractBinding(Identifier = "MBus", DefaultName = "Sample M-Bus meter")]
        public IMBus MBus { get; set; } = null!;

        public double? Frequency { get; private set; }

        public Exception? LastError { get; private set; }

        /// <summary>The receipt of the last read that completed, whichever way it ended.</summary>
        public MBusReceipt? LastReceipt { get; private set; }

        public SampleLogicBlock(ILogger logger) : base(logger)
        {
        }

        public void Poll()
        {
            MBus.Read(PrimaryAddress,
                      5,
                      this,
                      (readout, receipt) =>
                      {
                          Frequency = readout.Value(FrequencySel, MBusUnit.Hertz);
                          LastReceipt = receipt;
                      },
                      (error, receipt) =>
                      {
                          LastError = error;
                          LastReceipt = receipt;
                      });
        }

        protected override void Ready()
        {
            MBus.IsEnabled = true;
        }
    }
}
