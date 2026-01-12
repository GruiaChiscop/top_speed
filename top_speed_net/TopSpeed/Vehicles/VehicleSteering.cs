using System;

namespace TopSpeed.Vehicles
{
    internal static class VehicleSteering
    {
        public static void UpdateSteeringInput(
            ref VehicleDynamicsState state,
            float steerTurnRate,
            float steerReturnRate,
            float steerGamma,
            float steerLowDeg,
            float steerHighDeg,
            float steerSpeedKph,
            float steerSpeedExponent,
            int steeringCommand,
            float speedKph,
            float dt)
        {
            var desired = VehicleMath.Clamp(steeringCommand / 100.0f, -1f, 1f);
            var rate = Math.Abs(desired) > Math.Abs(state.SteerInput) ? steerTurnRate : steerReturnRate;
            state.SteerInput = VehicleMath.Approach(state.SteerInput, desired, rate * dt);

            var shaped = Math.Abs(state.SteerInput) <= 0f
                ? 0f
                : Math.Sign(state.SteerInput) * (float)Math.Pow(Math.Abs(state.SteerInput), steerGamma);
            var steerLimit = CalculateSteerLimit(steerLowDeg, steerHighDeg, steerSpeedKph, steerSpeedExponent, speedKph);
            state.SteerWheelAngleDeg = shaped * steerLimit;
            state.SteerWheelAngleRad = state.SteerWheelAngleDeg * ((float)Math.PI / 180.0f);
        }

        private static float CalculateSteerLimit(
            float steerLowDeg,
            float steerHighDeg,
            float steerSpeedKph,
            float steerSpeedExponent,
            float speedKph)
        {
            if (steerSpeedKph <= 0f)
                return steerLowDeg;
            var t = speedKph / steerSpeedKph;
            t = VehicleMath.Clamp(t, 0f, 1f);
            t = VehicleMath.SmoothStep(t);
            if (steerSpeedExponent > 0f)
                t = (float)Math.Pow(t, steerSpeedExponent);
            return VehicleMath.Lerp(steerLowDeg, steerHighDeg, t);
        }
    }
}
