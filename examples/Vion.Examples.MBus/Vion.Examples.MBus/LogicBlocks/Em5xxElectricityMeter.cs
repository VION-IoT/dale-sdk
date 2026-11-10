using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.MBus;

namespace Vion.Examples.MBus.LogicBlocks
{
    /// <summary>
    ///     Reads a Carlo Gavazzi EM5XX three-phase electricity meter over wired M-Bus. The meter sends five
    ///     telegrams; the values are picked out of them by meaning, declared once as selectors that read like the
    ///     meter's datasheet, and arrive scaled in the unit each property publishes.
    /// </summary>
    /// <remarks>
    ///     A value the readout does not carry is published as empty, never as zero: an incomplete readout (a
    ///     provider that reads fewer telegrams than the meter sends) leaves the later telegrams' values empty, and a
    ///     failed read empties them all.
    /// </remarks>
    [LogicBlock(Name = "EM5XX Electricity Meter (M-Bus)", Icon = "flashlight-line")]
    public class Em5xxElectricityMeter : LogicBlockBase
    {
        // The EM5XX's frame table: phases, tariffs and line-to-line values are told apart by subunit.
        private static readonly MBusSelector ImportSel = MBusSelector.Of(MBusQuantity.Energy);

        private static readonly MBusSelector PowerSel = MBusSelector.Of(MBusQuantity.Power);

        private static readonly MBusSelector CurrentL1Sel = MBusSelector.Of(MBusQuantity.Current, subunit: 1);

        private static readonly MBusSelector VoltageL1Sel = MBusSelector.Of(MBusQuantity.Voltage, subunit: 1);

        private static readonly MBusSelector ImportT1Sel = MBusSelector.Of(MBusQuantity.Energy, subunit: 6);

        private static readonly MBusSelector FrequencySel = MBusSelector.Of(MBusQuantity.Frequency);

        private static readonly MBusSelector OperatingTimeSel = MBusSelector.Of(MBusQuantity.OperatingTime, subunit: 2);

        [ServiceProviderContractBinding(Identifier = "MBus", DefaultName = "M-Bus")]
        public IMBus MBus { get; set; } = null!;

        [ServiceProperty(Title = "Primary address", Minimum = 0, Maximum = 250)]
        [Presentation(Group = PropertyGroup.Configuration)]
        public int PrimaryAddress { get; set; } = 1;

        [ServiceProperty(Title = "Import energy", Unit = "kWh")]
        public double? ImportEnergy { get; private set; }

        [ServiceProperty(Title = "Active power", Unit = "kW")]
        public double? ActivePower { get; private set; }

        [ServiceProperty(Title = "Current L1", Unit = "A")]
        public double? CurrentL1 { get; private set; }

        [ServiceProperty(Title = "Voltage L1-N", Unit = "V")]
        public double? VoltageL1 { get; private set; }

        [ServiceProperty(Title = "Import energy, tariff 1", Unit = "kWh")]
        public double? ImportT1 { get; private set; }

        [ServiceProperty(Title = "Frequency", Unit = "Hz")]
        public double? Frequency { get; private set; }

        [ServiceProperty(Title = "Run hours", Unit = "h")]
        public double? RunHours { get; private set; }

        [ServiceProperty(Title = "Meter error", Description = "The meter's status byte reports an error.")]
        [Presentation(Group = PropertyGroup.Status)]
        public bool MeterError { get; private set; }

        [ServiceProperty(Title = "Last error")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public string LastError { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Link", Description = "The M-Bus link to the meter, as the contract accumulates it from every read.")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public MBusLinkSummary Link { get; private set; }

        public Em5xxElectricityMeter(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            MBus.IsEnabled = true;
        }

        [Timer(60)]
        public void Poll()
        {
            MBus.Read(PrimaryAddress,
                      5,
                      this,
                      (readout, _) =>
                      {
                          ImportEnergy = readout.Value(ImportSel, MBusUnit.KilowattHour);
                          ActivePower = readout.Value(PowerSel, MBusUnit.Kilowatt);
                          CurrentL1 = readout.Value(CurrentL1Sel, MBusUnit.Ampere);
                          VoltageL1 = readout.Value(VoltageL1Sel, MBusUnit.Volt);
                          ImportT1 = readout.Value(ImportT1Sel, MBusUnit.KilowattHour);
                          Frequency = readout.Value(FrequencySel, MBusUnit.Hertz);
                          RunHours = readout.Value(OperatingTimeSel, MBusUnit.Hour);
                          MeterError = readout.Header.Status.HasError;
                          LastError = string.Empty;
                          Link = MBus.Link;
                      },
                      OnError);
        }

        private void OnError(Exception error, MBusReceipt receipt)
        {
            ImportEnergy = null;
            ActivePower = null;
            CurrentL1 = null;
            VoltageL1 = null;
            ImportT1 = null;
            Frequency = null;
            RunHours = null;
            LastError = $"{receipt.Outcome}: {error.Message}";
            Link = MBus.Link;
        }
    }
}
