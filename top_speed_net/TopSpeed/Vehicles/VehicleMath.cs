using System;

namespace TopSpeed.Vehicles
{
    internal static class VehicleMath
    {
        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        public static float SmoothStep(float t)
        {
            t = Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        public static float Approach(float value, float target, float delta)
        {
            if (value < target)
                return Math.Min(value + delta, target);
            if (value > target)
                return Math.Max(value - delta, target);
            return value;
        }
    }
}
