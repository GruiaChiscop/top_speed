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
        public int Acceleration { get; }
        public int Deceleration { get; }
        public int TopSpeed { get; }
        public int IdleFreq { get; }
        public int TopFreq { get; }
        public int ShiftFreq { get; }
        public int Gears { get; }
        public int Steering { get; }
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
            int acceleration,
            int deceleration,
            int topSpeed,
            int idleFreq,
            int topFreq,
            int shiftFreq,
            int gears,
            int steering,
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
            new VehicleParameters(VehiclePath(1, "vehicle1_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(1, "vehicle1_h.wav"), VehiclePath(1, "vehicle1_t.wav"), VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 1, 11, 40, 17500, 22050, 55000, 26000, 5, 160, 60),
            new VehicleParameters(VehiclePath(2, "vehicle2_e.wav"), VehiclePath(2, "vehicle2_s.wav"), VehiclePath(2, "vehicle2_h.wav"), VehiclePath(2, "vehicle2_t.wav"), VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 1, 13, 35, 18500, 22050, 60000, 35000, 5, 150, 55),
            new VehicleParameters(VehiclePath(3, "vehicle3_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(3, "vehicle3_h.wav"), null, VehiclePath(3, "vehicle3_c.wav"), VehiclePath(3, "vehicle3_cm.wav"), VehiclePath(3, "vehicle3_b.wav"), null, 1, 10, 35, 15100, 6000, 25000, 19000, 4, 150, 72),
            new VehicleParameters(VehiclePath(4, "vehicle4_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(4, "vehicle4_h.wav"), null, VehiclePath(3, "vehicle3_c.wav"), VehiclePath(3, "vehicle3_cm.wav"), VehiclePath(3, "vehicle3_b.wav"), null, 1, 12, 40, 17200, 6000, 27000, 20000, 6, 140, 56),
            new VehicleParameters(VehiclePath(5, "vehicle5_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(5, "vehicle5_h.wav"), null, VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 1, 12, 60, 24000, 6000, 33000, 27500, 4, 230, 80),
            new VehicleParameters(VehiclePath(6, "vehicle6_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(6, "vehicle6_h.wav"), null, VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(6, "vehicle6_b.wav"), null, 1, 9, 90, 26000, 7025, 40000, 32500, 6, 220, 95),
            new VehicleParameters(VehiclePath(7, "vehicle7_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(3, "vehicle3_h.wav"), null, VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(3, "vehicle3_b.wav"), null, 1, 13, 70, 21000, 6000, 26000, 21000, 5, 210, 65),
            new VehicleParameters(VehiclePath(8, "vehicle8_e.wav"), VehiclePath(1, "vehicle1_s.wav"), VehiclePath(6, "vehicle6_h.wav"), null, VehiclePath(1, "vehicle1_c.wav"), VehiclePath(1, "vehicle1_cm.wav"), VehiclePath(6, "vehicle6_b.wav"), null, 1, 11, 55, 23000, 10000, 45000, 34000, 5, 200, 70),
            new VehicleParameters(VehiclePath(9, "vehicle9_e.wav"), VehiclePath(9, "vehicle9_s.wav"), VehiclePath(9, "vehicle9_h.wav"), VehiclePath(9, "vehicle9_t.wav"), VehiclePath(9, "vehicle9_c.wav"), VehiclePath(9, "vehicle9_cm.wav"), VehiclePath(9, "vehicle9_b.wav"), VehiclePath(9, "vehicle9_f.wav"), 1, 8, 25, 18000, 22050, 30550, 22550, 5, 150, 85),
            new VehicleParameters(VehiclePath(10, "vehicle10_e.wav"), VehiclePath(10, "vehicle10_s.wav"), VehiclePath(10, "vehicle10_h.wav"), null, VehiclePath(10, "vehicle10_c.wav"), VehiclePath(10, "vehicle10_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 0, 15, 45, 20000, 22050, 60000, 35000, 5, 140, 50),
            new VehicleParameters(VehiclePath(11, "vehicle11_e.wav"), VehiclePath(11, "vehicle11_s.wav"), VehiclePath(10, "vehicle10_h.wav"), null, VehiclePath(10, "vehicle10_c.wav"), VehiclePath(10, "vehicle10_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), null, 0, 17, 40, 22000, 22050, 60000, 35000, 5, 130, 50),
            new VehicleParameters(VehiclePath(12, "vehicle12_e.wav"), VehiclePath(12, "vehicle12_s.wav"), VehiclePath(12, "vehicle12_h.wav"), VehiclePath(12, "vehicle12_t.wav"), VehiclePath(10, "vehicle10_c.wav"), VehiclePath(10, "vehicle10_cm.wav"), VehiclePath(1, "vehicle1_b.wav"), VehiclePath(12, "vehicle12_f.wav"), 0, 13, 45, 24000, 22050, 27550, 23550, 5, 150, 66)    
        };
    }
}
