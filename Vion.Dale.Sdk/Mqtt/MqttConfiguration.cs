using System;

namespace Vion.Dale.Sdk.Mqtt
{
    public static class MqttConfiguration
    {
        /// <summary>
        ///     Gets or sets the installation-specific part of the MQTT topic.
        /// </summary>
        /// <value>The MQTT installation topic prefix string.</value>
        /// <exception cref="InvalidOperationException">Thrown when getting the value before it has been initialized.</exception>
        /// <exception cref="ArgumentNullException">Thrown when attempting to set a null value.</exception>
        /// <remarks>
        ///     This value uniquely identifies the installation and is used as part of the complete topic structure.
        ///     The value is automatically set once during system startup and cannot be modified afterward.
        /// </remarks>
        public static string InstallationTopic
        {
            get => field ?? throw new InvalidOperationException($"{nameof(InstallationTopic)} has not been initialized.");

            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                if (field != null)
                {
                    return;
                }

                field = value;
            }
        }
    }
}