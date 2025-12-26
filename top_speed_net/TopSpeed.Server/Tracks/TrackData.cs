using TopSpeed.Server.Protocol;

namespace TopSpeed.Server.Tracks
{
    internal sealed class TrackData
    {
        public TrackData()
        {
        }

        public TrackData(bool userDefined, TrackWeather weather, TrackAmbience ambience, MultiplayerDefinition[] definitions)
        {
            UserDefined = userDefined;
            Weather = (byte)weather;
            Ambience = (byte)ambience;
            Definitions = definitions ?? System.Array.Empty<MultiplayerDefinition>();
            Length = (ushort)System.Math.Min(Definitions.Length, ProtocolConstants.MaxMultiTrackLength);
        }

        public bool UserDefined { get; set; }
        public byte Laps { get; set; } = 3;
        public byte Weather { get; set; }
        public byte Ambience { get; set; }
        public ushort Length { get; set; }
        public MultiplayerDefinition[] Definitions { get; set; } = System.Array.Empty<MultiplayerDefinition>();
    }
}
