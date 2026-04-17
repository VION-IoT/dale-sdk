using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Microsoft.Extensions.Logging;

namespace VionIotLibraryTemplate
{
    /// <summary>
    ///     Smart LED Controller - A comprehensive example demonstrating:
    ///     - Digital Input (Button) controls LED mode
    ///     - Digital Output (LED) can be controlled via property
    ///     - Properties for configuration and state
    ///     - Measuring points for monitoring
    ///     - Timer for automatic blinking mode
    /// </summary>
    public class SmartLedController : LogicBlockBase
    {
        /// <summary>
        ///     LED operating modes
        /// </summary>
        public enum LedMode
        {
            /// <summary>Manual control via LedEnabled property</summary>
            Manual = 0,

            /// <summary>Automatically blinks based on BlinkIntervalSeconds</summary>
            AutoBlink = 1,

            /// <summary>LED follows button state (on when pressed)</summary>
            ButtonControlled = 2,
        }

        private readonly ILogger _logger;

        private bool _ledEnabled;

        private LedMode _mode = LedMode.Manual;

        private int _tickCount;

        private int _totalBlinks;

        // === I/O ===

        [ServiceProviderContract("Button")]
        public IDigitalInput Button { get; set; } = null!;

        [ServiceProviderContract("LED")]
        public IDigitalOutput Led { get; set; } = null!;

        // === Service Properties (Writable) ===

        /// <summary>
        ///     Controls the LED operating mode
        /// </summary>
        [ServiceProperty]
        public LedMode Mode
        {
            get => _mode;

            set
            {
                if (_mode != value) // on change
                {
                    _mode = value;
                    _logger.LogInformation("LED Mode changed to: {Mode}", value);

                    // When switching to manual mode, turn off LED
                    if (value == LedMode.Manual)
                    {
                        SetLedState(false);
                    }
                }
            }
        }

        /// <summary>
        ///     Manually control LED on/off (only works in Manual mode)
        /// </summary>
        [ServiceProperty]
        public bool LedEnabled
        {
            get => _ledEnabled;

            set
            {
                if (Mode == LedMode.Manual)
                {
                    SetLedState(value);
                }
                else
                {
                    _logger.LogWarning("Cannot manually control LED in {Mode} mode", Mode);
                }
            }
        }

        /// <summary>
        ///     Blink interval in seconds (only used in AutoBlink mode)
        /// </summary>
        [ServiceProperty]
        public double BlinkIntervalSeconds { get; set; } = 1.0;

        // === Measuring Points (Read-only) ===

        /// <summary>
        ///     Current button state (pressed = true)
        /// </summary>
        [ServiceMeasuringPoint]
        public bool ButtonPressed { get; private set; }

        /// <summary>
        ///     Total number of times the LED has blinked
        /// </summary>
        [ServiceProperty]
        [ServiceMeasuringPoint]
        public int TotalBlinks
        {
            get => _totalBlinks;

            private set
            {
                if (_totalBlinks != value) // on change
                {
                    _totalBlinks = value;
                    _logger.LogDebug("Total blinks: {Count}", value);
                }
            }
        }

        /// <summary>
        ///     How many times the button was pressed
        /// </summary>
        [ServiceMeasuringPoint]
        public int ButtonPressCount { get; private set; }

        public SmartLedController(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        /// <summary>
        ///     Timer that runs every second for auto-blink mode
        /// </summary>
        [Timer(1)]
        public void AutoBlinkTick()
        {
            if (Mode == LedMode.AutoBlink)
            {
                _tickCount++;

                // Blink based on configured interval
                var shouldBlink = _tickCount % (int)(BlinkIntervalSeconds * 2) < BlinkIntervalSeconds;

                if (_ledEnabled != shouldBlink)
                {
                    SetLedState(shouldBlink);

                    if (shouldBlink) // Count on rising edge
                    {
                        TotalBlinks++;
                    }
                }
            }
        }

        protected override void Ready()
        {
            _logger.LogInformation("🚀 SmartLedController is ready!");

            // Button press cycles through modes: Manual → AutoBlink → ButtonControlled → Manual
            Button.InputChanged += (_, pressed) =>
                                   {
                                       ButtonPressed = pressed;

                                       if (pressed) // Rising edge
                                       {
                                           ButtonPressCount++;
                                           CycleModeOnButtonPress();
                                       }
                                   };

            // Monitor LED state changes
            Led.OutputChanged += (_, state) => { _logger.LogInformation("💡 LED is now: {State}", state ? "ON" : "OFF"); };
        }

        /// <summary>
        ///     Cycles through LED modes when button is pressed
        /// </summary>
        private void CycleModeOnButtonPress()
        {
            Mode = Mode switch
            {
                LedMode.Manual => LedMode.AutoBlink,
                LedMode.AutoBlink => LedMode.ButtonControlled,
                _ => LedMode.Manual,
            };
        }

        /// <summary>
        ///     Sets the LED state and updates internal tracking
        /// </summary>
        private void SetLedState(bool state)
        {
            _ledEnabled = state;
            Led.Set(state);
        }
    }
}