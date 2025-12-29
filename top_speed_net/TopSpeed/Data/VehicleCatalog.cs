using System.IO;
using TopSpeed.Protocol;

namespace TopSpeed.Data
{
    internal sealed class VehicleParameters
    {
        private readonly string?[] _sounds = new string?[8];

        public string? GetSoundPath(VehicleAction action) => _sounds[(int)action];

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
            _sounds[(int)VehicleAction.Engine] = engineSound;
            _sounds[(int)VehicleAction.Start] = startSound;
            _sounds[(int)VehicleAction.Horn] = hornSound;
            _sounds[(int)VehicleAction.Throttle] = throttleSound;
            _sounds[(int)VehicleAction.Crash] = crashSound;
            _sounds[(int)VehicleAction.CrashMono] = monoCrashSound;
            _sounds[(int)VehicleAction.Brake] = brakeSound;
            _sounds[(int)VehicleAction.Backfire] = backfireSound;

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

        public static readonly VehicleParameters[] Vehicles =
        {
            // Null values now automatically fallback to the vehicle's folder version, then to the 'default' folder version.
            new VehicleParameters(null, null, null, null, null, null, null, null, 1, 0.11f, 0.40f, 175.0f, 22050, 55000, 26000, 5, 1.60f, 60),
            new VehicleParameters(null, null, null, null, null, null, null, null, 1, 0.13f, 0.35f, 185.0f, 22050, 60000, 35000, 5, 1.50f, 55),
            new VehicleParameters(null, null, null, null, null, null, null, null, 1, 0.10f, 0.35f, 151.0f, 6000, 25000, 19000, 4, 1.50f, 72),
            new VehicleParameters(null, null, null, null, null, null, null, null, 1, 0.12f, 0.40f, 172.0f, 6000, 27000, 20000, 6, 1.40f, 56),
            new VehicleParameters(null, null, null, null, null, null, null, null, 1, 0.12f, 0.60f, 240.0f, 6000, 33000, 27500, 4, 2.30f, 80),
            new VehicleParameters(null, null, null, null, null, null, null, null, 1, 0.09f, 0.90f, 260.0f, 7025, 40000, 32500, 6, 2.20f, 95),
            new VehicleParameters(null, null, null, null, null, null, null, null, 1, 0.13f, 0.70f, 210.0f, 6000, 26000, 21000, 5, 2.10f, 65),
            new VehicleParameters(null, null, null, null, null, null, null, null, 1, 0.11f, 0.55f, 230.0f, 10000, 45000, 34000, 5, 2.00f, 70),
            new VehicleParameters(null, null, null, null, null, null, null, null, 1, 0.08f, 0.25f, 180.0f, 22050, 30550, 22550, 5, 1.50f, 85),
            new VehicleParameters(null, null, null, null, null, null, null, null, 0, 0.15f, 0.45f, 200.0f, 22050, 60000, 35000, 5, 1.40f, 50),
            new VehicleParameters(null, null, null, null, null, null, null, null, 0, 0.17f, 0.40f, 220.0f, 22050, 60000, 35000, 5, 1.30f, 50),
            new VehicleParameters(null, null, null, null, null, null, null, null, 0, 0.13f, 0.45f, 240.0f, 22050, 27550, 23550, 5, 1.50f, 66)    
        };
    }
}
