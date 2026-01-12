using System;

namespace TopSpeed.Common
{
    internal static class CompassHeading
    {
        private static readonly string[] Directions =
        {
            "North",
            "North north east",
            "North east",
            "East north east",
            "East",
            "East south east",
            "South east",
            "South south east",
            "South",
            "South south west",
            "South west",
            "West south west",
            "West",
            "West north west",
            "North west",
            "North north west"
        };

        public static string FormatHeading(float degrees)
        {
            var normalized = NormalizeDegrees(degrees);
            var index = (int)Math.Round(normalized / 22.5f) % Directions.Length;
            var wholeDegrees = (int)Math.Floor(normalized);
            if (wholeDegrees >= 360)
                wholeDegrees = 0;
            return $"{Directions[index]} at {wholeDegrees} degrees";
        }

        private static float NormalizeDegrees(float degrees)
        {
            degrees %= 360f;
            if (degrees < 0f)
                degrees += 360f;
            return degrees;
        }
    }
}
