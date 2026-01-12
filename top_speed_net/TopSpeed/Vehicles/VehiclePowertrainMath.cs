using System;

namespace TopSpeed.Vehicles
{
    internal struct VehiclePowertrainParameters
    {
        public float MassKg;
        public float WheelRadiusM;
        public float FinalDriveRatio;
        public float DrivetrainEfficiency;
        public float EngineBrakingTorqueNm;
        public float EngineBraking;
        public float IdleRpm;
        public float RevLimiter;
        public float LaunchRpm;
        public float PowerFactor;
        public float PeakTorqueNm;
        public float PeakTorqueRpm;
        public float IdleTorqueNm;
        public float RedlineTorqueNm;
        public float TireGripCoefficient;
        public float BrakeStrength;
        public float DragCoefficient;
        public float FrontalAreaM2;
        public float RollingResistanceCoefficient;
    }

    internal readonly struct AutoShiftDecision
    {
        public AutoShiftDecision(bool shouldShift, int targetGear)
        {
            ShouldShift = shouldShift;
            TargetGear = targetGear;
        }

        public bool ShouldShift { get; }
        public int TargetGear { get; }
    }

    internal static class VehiclePowertrainMath
    {
        private const float AutoShiftHysteresis = 0.05f;

        public static float CalculateDriveRpm(
            EngineModel engine,
            int gear,
            float speedMps,
            float throttle,
            in VehiclePowertrainParameters p)
        {
            var wheelCircumference = p.WheelRadiusM * 2.0f * (float)Math.PI;
            var gearRatio = engine.GetGearRatio(gear);
            var speedBasedRpm = wheelCircumference > 0f
                ? (speedMps / wheelCircumference) * 60f * gearRatio * p.FinalDriveRatio
                : 0f;
            var launchTarget = p.IdleRpm + (throttle * (p.LaunchRpm - p.IdleRpm));
            var rpm = Math.Max(speedBasedRpm, launchTarget);
            if (rpm < p.IdleRpm)
                rpm = p.IdleRpm;
            if (rpm > p.RevLimiter)
                rpm = p.RevLimiter;
            return rpm;
        }

        public static float CalculateEngineTorqueNm(float rpm, in VehiclePowertrainParameters p)
        {
            if (p.PeakTorqueNm <= 0f)
                return 0f;
            var clampedRpm = Math.Max(p.IdleRpm, Math.Min(p.RevLimiter, rpm));
            if (clampedRpm <= p.PeakTorqueRpm)
            {
                var denom = p.PeakTorqueRpm - p.IdleRpm;
                var t = denom > 0f ? (clampedRpm - p.IdleRpm) / denom : 0f;
                return SmoothStep(p.IdleTorqueNm, p.PeakTorqueNm, t);
            }

            var upperDenom = p.RevLimiter - p.PeakTorqueRpm;
            var t2 = upperDenom > 0f ? (clampedRpm - p.PeakTorqueRpm) / upperDenom : 0f;
            return SmoothStep(p.PeakTorqueNm, p.RedlineTorqueNm, t2);
        }

        public static float CalculateBrakeDecel(float brakeInput, float surfaceDecelMod, in VehiclePowertrainParameters p)
        {
            if (brakeInput <= 0f)
                return 0f;
            var grip = Math.Max(0.1f, p.TireGripCoefficient * surfaceDecelMod);
            var decelMps2 = brakeInput * p.BrakeStrength * grip * 9.80665f;
            return decelMps2 * 3.6f;
        }

        public static float CalculateEngineBrakingDecel(
            EngineModel engine,
            int gear,
            float surfaceDecelMod,
            in VehiclePowertrainParameters p)
        {
            if (p.EngineBrakingTorqueNm <= 0f || p.MassKg <= 0f || p.WheelRadiusM <= 0f)
                return 0f;
            var rpmRange = p.RevLimiter - p.IdleRpm;
            if (rpmRange <= 0f)
                return 0f;
            var rpmFactor = (engine.Rpm - p.IdleRpm) / rpmRange;
            if (rpmFactor <= 0f)
                return 0f;
            rpmFactor = Math.Max(0f, Math.Min(1f, rpmFactor));
            var gearRatio = engine.GetGearRatio(gear);
            var drivelineTorque = p.EngineBrakingTorqueNm * p.EngineBraking * rpmFactor;
            var wheelTorque = drivelineTorque * gearRatio * p.FinalDriveRatio * p.DrivetrainEfficiency;
            var wheelForce = wheelTorque / p.WheelRadiusM;
            var decelMps2 = (wheelForce / p.MassKg) * surfaceDecelMod;
            return Math.Max(0f, decelMps2 * 3.6f);
        }

        public static float SpeedToRpm(EngineModel engine, int gear, float speedMps, in VehiclePowertrainParameters p)
        {
            var wheelCircumference = p.WheelRadiusM * 2.0f * (float)Math.PI;
            if (wheelCircumference <= 0f)
                return 0f;
            var gearRatio = engine.GetGearRatio(gear);
            return (speedMps / wheelCircumference) * 60f * gearRatio * p.FinalDriveRatio;
        }

        public static float ComputeNetAccelForGear(
            EngineModel engine,
            int gear,
            int gearCount,
            float speedMps,
            float throttle,
            float surfaceTractionMod,
            float longitudinalGripFactor,
            in VehiclePowertrainParameters p)
        {
            var rpm = SpeedToRpm(engine, gear, speedMps, p);
            if (rpm <= 0f)
                return float.NegativeInfinity;
            if (rpm > p.RevLimiter && gear < gearCount)
                return float.NegativeInfinity;

            var engineTorque = CalculateEngineTorqueNm(rpm, p) * throttle * p.PowerFactor;
            var gearRatio = engine.GetGearRatio(gear);
            var wheelTorque = engineTorque * gearRatio * p.FinalDriveRatio * p.DrivetrainEfficiency;
            var wheelForce = wheelTorque / p.WheelRadiusM;
            var tractionLimit = p.TireGripCoefficient * surfaceTractionMod * p.MassKg * 9.80665f;
            if (wheelForce > tractionLimit)
                wheelForce = tractionLimit;
            wheelForce *= longitudinalGripFactor;

            var dragForce = 0.5f * 1.225f * p.DragCoefficient * p.FrontalAreaM2 * speedMps * speedMps;
            var rollingForce = p.RollingResistanceCoefficient * p.MassKg * 9.80665f;
            var netForce = wheelForce - dragForce - rollingForce;
            return netForce / p.MassKg;
        }

        public static AutoShiftDecision SelectAutomaticGear(
            EngineModel engine,
            int currentGear,
            int gearCount,
            float speedMps,
            float throttle,
            float surfaceTractionMod,
            float longitudinalGripFactor,
            in VehiclePowertrainParameters p)
        {
            if (gearCount <= 1)
                return new AutoShiftDecision(false, currentGear);

            var currentAccel = ComputeNetAccelForGear(engine, currentGear, gearCount, speedMps, throttle, surfaceTractionMod, longitudinalGripFactor, p);
            var bestGear = currentGear;
            var bestAccel = currentAccel;

            if (currentGear < gearCount)
            {
                var upAccel = ComputeNetAccelForGear(engine, currentGear + 1, gearCount, speedMps, throttle, surfaceTractionMod, longitudinalGripFactor, p);
                if (upAccel > bestAccel)
                {
                    bestAccel = upAccel;
                    bestGear = currentGear + 1;
                }
            }

            if (currentGear > 1)
            {
                var downAccel = ComputeNetAccelForGear(engine, currentGear - 1, gearCount, speedMps, throttle, surfaceTractionMod, longitudinalGripFactor, p);
                if (downAccel > bestAccel)
                {
                    bestAccel = downAccel;
                    bestGear = currentGear - 1;
                }
            }

            var currentRpm = SpeedToRpm(engine, currentGear, speedMps, p);
            if (currentGear < gearCount && currentRpm >= p.RevLimiter * 0.995f)
                return new AutoShiftDecision(true, currentGear + 1);

            var shiftRpm = p.IdleRpm + (p.RevLimiter - p.IdleRpm) * 0.35f;
            if (currentGear > 1 && currentRpm < shiftRpm)
                return new AutoShiftDecision(true, currentGear - 1);

            if (bestGear != currentGear && bestAccel > currentAccel * (1f + AutoShiftHysteresis))
                return new AutoShiftDecision(true, bestGear);

            return new AutoShiftDecision(false, currentGear);
        }

        public static int CalculateAcceleration(EngineModel engine, int gear, float speedKph)
        {
            var gearRange = engine.GetGearRangeKmh(gear);
            var gearMin = engine.GetGearMinSpeedKmh(gear);
            var gearCenter = gearMin + (gearRange * 0.18f);
            var speedDiff = speedKph - gearCenter;
            var relSpeedDiff = speedDiff / gearRange;
            if (Math.Abs(relSpeedDiff) < 1.9f)
            {
                var acceleration = (int)(100.0f * (0.5f + Math.Cos(relSpeedDiff * Math.PI * 0.5f)));
                return acceleration < 5 ? 5 : acceleration;
            }

            var minAcceleration = (int)(100.0f * (0.5f + Math.Cos(0.95f * Math.PI)));
            return minAcceleration < 5 ? 5 : minAcceleration;
        }

        private static float SmoothStep(float a, float b, float t)
        {
            var clamped = Math.Max(0f, Math.Min(1f, t));
            clamped = clamped * clamped * (3f - 2f * clamped);
            return a + (b - a) * clamped;
        }
    }
}
