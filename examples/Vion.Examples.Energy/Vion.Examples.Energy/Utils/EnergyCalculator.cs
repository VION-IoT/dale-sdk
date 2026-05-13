using System;

namespace Vion.Examples.Energy.Utils
{
    /// <summary>
    ///     Utility class for energy calculations.
    /// </summary>
    public static class EnergyCalculator
    {
        /// <summary>
        ///     Calculates the energy increment using trapezoidal integration.
        /// </summary>
        public static double CalculateEnergyIncrement(double previousPower, double currentPower, DateTime previousTime, DateTime currentTime)
        {
            var timeIntervalHours = (currentTime - previousTime).TotalHours;

            // Trapezoidal integration: Energy = (P1 + P2) / 2 * deltaT
            var averagePower = (previousPower + currentPower) / 2;
            var energyIncrement = averagePower * timeIntervalHours;

            return energyIncrement;
        }
    }
}