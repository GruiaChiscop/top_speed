using System.IO;

namespace TopSpeed.Data
{
    internal sealed class VehicleParameters
    {
        public string? EngineSound { get; }
        public string? StartSound { get; }
        public string? HornSound { get; }
        public string? ThrottleSound { get; }
        public string? CrashSound { get; }
        public string? MonoCrashSound { get; }
        public string? BrakeSound { get; }
        public string? BackfireSound { get; }
        public int HasWipers { get; }
        public float Acceleration { get; }
        public float Deceleration { get; }
        public float TopSpeed { get; }
        public int IdleFreq { get; }
        public int TopFreq { get; }
        public int ShiftFreq { get; }
        public int Gears { get; }
        public float Steering { get; }
        public int SteeringFactor { get; }

        public VehicleParameters(
            string? engineSound,
            string? startSound,
            string? hornSound,
            string? throttleSound,
            string? crashSound,
            string? monoCrashSound,
            string? brakeSound,
            string? backfireSound,
            int hasWipers,
            float acceleration,
            float deceleration,
            float topSpeed,
            int idleFreq,
            int topFreq,
            int shiftFreq,
            int gears,
            float steering,
            int steeringFactor)
        {
            EngineSound = engineSound;
            StartSound = startSound;
            HornSound = hornSound;
            ThrottleSound = throttleSound;
            CrashSound = crashSound;
            MonoCrashSound = monoCrashSound;
            BrakeSound = brakeSound;
            BackfireSound = backfireSound;
            HasWipers = hasWipers;
            Acceleration = acceleration;
            Deceleration = deceleration;
            TopSpeed = topSpeed;
            IdleFreq = idleFreq;
            TopFreq = topFreq;
            ShiftFreq = shiftFreq;
            Gears = gears;
            Steering = steering;
            SteeringFactor = steeringFactor;
        }
    }

    internal static class VehicleCatalog
    {
        public const int VehicleCount = 12;

        private static string VehiclePath(int vehicleNumber, string fileName)
        {
            return Path.Combine($"Vehicle{vehicleNumber}", fileName);
        }

        public static readonly VehicleParameters[] Vehicles =
        {
            new VehicleParameters(VehiclePath(1, "vehicle1_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(1, "vehicle1_h.wav"), VehiclePath(1, "vehicle1_t.wav"), VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 1, 0.11f, 0.40f, 175.0f, 22050, 55000, 26000, 5, 1.60f, 60),
            new VehicleParameters(VehiclePath(2, "vehicle2_e.wav"), VehiclePath(2, "vehicle2_s.wav"), VehiclePath(2, "vehicle2_h.wav"), VehiclePath(2, "vehicle2_t.wav"), VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 1, 0.13f, 0.35f, 185.0f, 22050, 60000, 35000, 5, 1.50f, 55),
            new VehicleParameters(VehiclePath(3, "vehicle3_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(3, "vehicle3_h.wav"), null, VehiclePath(3, "vehicle3_c.wav"), VehiclePath(3, "vehicle3_cm.wav"), VehiclePath(3, "vehicle3_b.wav"), null, 1, 0.10f, 0.35f, 151.0f, 6000, 25000, 19000, 4, 1.50f, 72),
            new VehicleParameters(VehiclePath(4, "vehicle4_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(4, "vehicle4_h.wav"), null, VehiclePath(3, "vehicle3_c.wav"), VehiclePath(3, "vehicle3_cm.wav"), VehiclePath(3, "vehicle3_b.wav"), null, 1, 0.12f, 0.40f, 172.0f, 6000, 27000, 20000, 6, 1.40f, 56),
            new VehicleParameters(VehiclePath(5, "vehicle5_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(5, "vehicle5_h.wav"), null, VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 1, 0.12f, 0.60f, 240.0f, 6000, 33000, 27500, 4, 2.30f, 80),
            new VehicleParameters(VehiclePath(6, "vehicle6_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(6, "vehicle6_h.wav"), null, VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(6, "vehicle6_b.wav"), null, 1, 0.09f, 0.90f, 260.0f, 7025, 40000, 32500, 6, 2.20f, 95),
            new VehicleParameters(VehiclePath(7, "vehicle7_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(3, "vehicle3_h.wav"), null, VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(3, "vehicle3_b.wav"), null, 1, 0.13f, 0.70f, 210.0f, 6000, 26000, 21000, 5, 2.10f, 65),
            new VehicleParameters(VehiclePath(8, "vehicle8_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(6, "vehicle6_h.wav"), null, VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(6, "vehicle6_b.wav"), null, 1, 0.11f, 0.55f, 230.0f, 10000, 45000, 34000, 5, 2.00f, 70),
            new VehicleParameters(VehiclePath(9, "vehicle9_e.wav"), VehiclePath(9, "vehicle9_s.wav"), VehiclePath(9, "vehicle9_h.wav"), VehiclePath(9, "vehicle9_t.wav"), VehiclePath(9, "vehicle9_c.wav"), VehiclePath(9, "vehicle9_cm.wav"), VehiclePath(9, "vehicle9_b.wav"), VehiclePath(9, "vehicle9_f.wav"), 1, 0.08f, 0.25f, 180.0f, 22050, 30550, 22550, 5, 1.50f, 85),
            new VehicleParameters(VehiclePath(10, "vehicle10_e.wav"), VehiclePath(10, "vehicle10_s.wav"), VehiclePath(10, "vehicle10_h.wav"), null, VehiclePath(10, "vehicle10_c.wav"), VehiclePath(10, "vehicle10_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 0, 0.15f, 0.45f, 200.0f, 22050, 60000, 35000, 5, 1.40f, 50),
            new VehicleParameters(VehiclePath(11, "vehicle11_e.wav"), VehiclePath(11, "vehicle11_s.wav"), VehiclePath(10, "vehicle10_h.wav"), null, VehiclePath(10, "vehicle10_c.wav"), VehiclePath(10, "vehicle10_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 0, 0.17f, 0.40f, 220.0f, 22050, 60000, 35000, 5, 1.30f, 50),
            new VehicleParameters(VehiclePath(12, "vehicle12_e.wav"), VehiclePath(12, "vehicle12_s.wav"), VehiclePath(12, "vehicle12_h.wav"), VehiclePath(12, "vehicle12_t.wav"), VehiclePath(10, "vehicle10_c.wav"), VehiclePath(10, "vehicle10_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), VehiclePath(12, "vehicle12_f.wav"), 0, 0.13f, 0.45f, 240.0f, 22050, 27550, 23550, 5, 1.50f, 66)    
        };
    }
}