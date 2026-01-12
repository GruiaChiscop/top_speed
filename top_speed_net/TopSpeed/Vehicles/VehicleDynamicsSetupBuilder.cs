using System;

namespace TopSpeed.Vehicles
{
    internal readonly struct VehicleDynamicsSetup
    {
        public VehicleDynamicsSetup(
            VehicleDynamicsParameters fourWheel,
            BicycleDynamicsParameters bicycle)
        {
            FourWheel = fourWheel;
            Bicycle = bicycle;
        }

        public VehicleDynamicsParameters FourWheel { get; }
        public BicycleDynamicsParameters Bicycle { get; }
    }

    internal static class VehicleDynamicsSetupBuilder
    {
        public static VehicleDynamicsSetup Build(
            VehicleDefinition definition,
            float massKg,
            float tireGripCoefficient,
            float lateralGripCoefficient,
            float dragCoefficient,
            float frontalAreaM2,
            float rollingResistanceCoefficient,
            float topSpeedKph,
            float wheelbaseM,
            float maxSteerDeg,
            float widthM,
            float lengthM)
        {
            var baseTurnRate = Math.Max(1.0f, definition.SteeringFactor / 40.0f);
            var steerTurnRate = definition.SteerInputRate > 0f
                ? definition.SteerInputRate
                : 1.6f + baseTurnRate * 0.6f;
            var steerReturnRate = definition.SteerReturnRate > 0f
                ? definition.SteerReturnRate
                : steerTurnRate * 1.7f;
            var steerGamma = definition.SteerGamma > 0.1f ? definition.SteerGamma : 1.9f;
            var steerLowDeg = definition.MaxSteerLowDeg > 0f ? definition.MaxSteerLowDeg : maxSteerDeg;
            var steerHighDeg = definition.MaxSteerHighDeg > 0f
                ? definition.MaxSteerHighDeg
                : Math.Max(5f, Math.Min(15f, maxSteerDeg * 0.28f));
            var steerSpeedKph = definition.SteerSpeedKph > 0f ? definition.SteerSpeedKph : Math.Max(60f, topSpeedKph * 0.5f);
            var steerSpeedExponent = definition.SteerSpeedExponent > 0f ? definition.SteerSpeedExponent : 1.7f;
            var cgHeight = definition.CgHeightM > 0f ? definition.CgHeightM : 0.55f;
            var frontWeightBias = definition.WeightDistributionFront > 0f
                ? Math.Max(0.35f, Math.Min(0.65f, definition.WeightDistributionFront))
                : 0.52f;
            var frontBrakeBias = definition.BrakeBiasFront > 0f
                ? Math.Max(0.5f, Math.Min(0.75f, definition.BrakeBiasFront))
                : 0.62f;
            var driveBiasFront = definition.DriveBiasFront > 0f
                ? Math.Max(0f, Math.Min(1f, definition.DriveBiasFront))
                : 0.5f;

            var cgToFront = 0f;
            var cgToRear = 0f;
            if (definition.CgToFrontAxleM > 0f && definition.CgToRearAxleM > 0f)
            {
                cgToFront = definition.CgToFrontAxleM;
                cgToRear = definition.CgToRearAxleM;
            }
            else
            {
                cgToRear = frontWeightBias * wheelbaseM;
                cgToFront = Math.Max(0.01f, wheelbaseM - cgToRear);
            }
            if (cgToFront + cgToRear <= 0.01f)
            {
                cgToFront = wheelbaseM * 0.5f;
                cgToRear = wheelbaseM * 0.5f;
            }

            var baseCornering = massKg * 9.80665f * tireGripCoefficient * lateralGripCoefficient;
            var corneringStiffnessFront = definition.CorneringStiffnessFront > 0f
                ? definition.CorneringStiffnessFront
                : baseCornering * 5.0f;
            var corneringStiffnessRear = definition.CorneringStiffnessRear > 0f
                ? definition.CorneringStiffnessRear
                : baseCornering * 5.5f;
            var yawInertia = definition.YawInertiaKgM2 > 0f
                ? definition.YawInertiaKgM2
                : (massKg * (lengthM * lengthM + widthM * widthM)) / 12.0f;
            var trackWidth = definition.TrackWidthM > 0f ? definition.TrackWidthM : Math.Max(0.8f, widthM * 0.9f);
            var rollStiffnessFront = definition.RollStiffnessFrontFraction > 0f
                ? Math.Max(0.2f, Math.Min(0.8f, definition.RollStiffnessFrontFraction))
                : frontWeightBias;
            var tireLoadSensitivity = definition.TireLoadSensitivity > 0f
                ? Math.Max(0.01f, Math.Min(0.4f, definition.TireLoadSensitivity))
                : 0.12f;
            var downforceCoefficient = definition.DownforceCoefficient > 0f ? definition.DownforceCoefficient : 0f;
            var downforceFrontBias = definition.DownforceFrontBias > 0f
                ? Math.Max(0.2f, Math.Min(0.8f, definition.DownforceFrontBias))
                : frontWeightBias;
            var longStiffnessFront = definition.LongitudinalStiffnessFront > 0f ? definition.LongitudinalStiffnessFront : 10f;
            var longStiffnessRear = definition.LongitudinalStiffnessRear > 0f ? definition.LongitudinalStiffnessRear : 10f;

            var fourWheel = new VehicleDynamicsParameters
            {
                MassKg = massKg,
                WheelbaseM = wheelbaseM,
                TrackWidthM = trackWidth,
                CgHeightM = cgHeight,
                CgToFrontM = cgToFront,
                CgToRearM = cgToRear,
                FrontWeightBias = frontWeightBias,
                FrontBrakeBias = frontBrakeBias,
                DriveBiasFront = driveBiasFront,
                YawInertiaKgM2 = yawInertia,
                CorneringStiffnessFront = corneringStiffnessFront,
                CorneringStiffnessRear = corneringStiffnessRear,
                DragCoefficient = dragCoefficient,
                FrontalAreaM2 = frontalAreaM2,
                RollingResistanceCoefficient = rollingResistanceCoefficient,
                RollStiffnessFrontFraction = rollStiffnessFront,
                TireLoadSensitivity = tireLoadSensitivity,
                DownforceCoefficient = downforceCoefficient,
                DownforceFrontBias = downforceFrontBias,
                LongitudinalStiffnessFront = longStiffnessFront,
                LongitudinalStiffnessRear = longStiffnessRear,
                SteerTurnRate = steerTurnRate,
                SteerReturnRate = steerReturnRate,
                SteerGamma = steerGamma,
                SteerLowDeg = steerLowDeg,
                SteerHighDeg = steerHighDeg,
                SteerSpeedKph = steerSpeedKph,
                SteerSpeedExponent = steerSpeedExponent,
                MaxSpeedKph = topSpeedKph
            };

            var bicycle = new BicycleDynamicsParameters
            {
                MassKg = massKg,
                WheelbaseM = wheelbaseM,
                CgHeightM = cgHeight,
                CgToFrontM = cgToFront,
                CgToRearM = cgToRear,
                FrontWeightBias = frontWeightBias,
                FrontBrakeBias = frontBrakeBias,
                DriveBiasFront = driveBiasFront,
                YawInertiaKgM2 = yawInertia,
                CorneringStiffnessFront = corneringStiffnessFront,
                CorneringStiffnessRear = corneringStiffnessRear,
                DragCoefficient = dragCoefficient,
                FrontalAreaM2 = frontalAreaM2,
                RollingResistanceCoefficient = rollingResistanceCoefficient,
                SteerTurnRate = steerTurnRate,
                SteerReturnRate = steerReturnRate,
                SteerGamma = steerGamma,
                SteerLowDeg = steerLowDeg,
                SteerHighDeg = steerHighDeg,
                SteerSpeedKph = steerSpeedKph,
                SteerSpeedExponent = steerSpeedExponent,
                MaxSpeedKph = topSpeedKph,
                TireLoadSensitivity = tireLoadSensitivity,
                DownforceCoefficient = downforceCoefficient,
                DownforceFrontBias = downforceFrontBias,
                LongitudinalStiffnessFront = longStiffnessFront,
                LongitudinalStiffnessRear = longStiffnessRear
            };

            return new VehicleDynamicsSetup(fourWheel, bicycle);
        }
    }
}
