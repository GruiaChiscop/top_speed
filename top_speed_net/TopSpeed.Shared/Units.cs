using System;

namespace TopSpeed.Common
{
    public static class Units
    {
        public const float LegacyUnitsPerMeter = 100.0f;
        public const float MetersPerSecondToKphFactor = 3.6f;

        public static float LegacyLengthToMeters(float legacyLength)
        {
            return legacyLength / LegacyUnitsPerMeter;
        }

        public static float LegacySpeedToMetersPerSecond(float legacySpeed)
        {
            return legacySpeed / LegacyUnitsPerMeter;
        }

        public static float KphToMetersPerSecond(float kph)
        {
            return kph / MetersPerSecondToKphFactor;
        }

        public static float MetersPerSecondToKph(float metersPerSecond)
        {
            return metersPerSecond * MetersPerSecondToKphFactor;
        }
    }
}