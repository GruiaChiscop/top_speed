using TopSpeed.Protocol;

namespace TopSpeed.Vehicles
{
    internal sealed class VehicleDefinition
    {
        public CarType CarType { get; set; }
        public bool UserDefined { get; set; }
        public string? CustomFile { get; set; }
        public float Acceleration { get; set; }
        public float Deceleration { get; set; }
        public float TopSpeed { get; set; }
        public int IdleFreq { get; set; }
        public int TopFreq { get; set; }
        public int ShiftFreq { get; set; }
        public int Gears { get; set; }
        public float Steering { get; set; }
        public int SteeringFactor { get; set; }
        public int HasWipers { get; set; }

        private readonly string?[] _sounds = new string?[8];

        public string? GetSoundPath(VehicleAction action) => _sounds[(int)action];
        public void SetSoundPath(VehicleAction action, string? path) => _sounds[(int)action] = path;
    }
}