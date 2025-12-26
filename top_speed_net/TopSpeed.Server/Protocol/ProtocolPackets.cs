using System;

namespace TopSpeed.Server.Protocol
{
    internal struct PlayerRaceData
    {
        public int PositionX;
        public int PositionY;
        public ushort Speed;
        public int Frequency;
    }

    internal struct MultiplayerDefinition
    {
        public byte Type { get; set; }
        public byte Surface { get; set; }
        public byte Noise { get; set; }
        public uint Length { get; set; }
    }

    internal struct PacketHeader
    {
        public byte Version;
        public Command Command;
    }

    internal sealed class PacketPlayer
    {
        public uint PlayerId;
        public byte PlayerNumber;
    }

    internal sealed class PacketPlayerState
    {
        public uint PlayerId;
        public byte PlayerNumber;
        public PlayerState State;
    }

    internal sealed class PacketPlayerData
    {
        public uint PlayerId;
        public byte PlayerNumber;
        public CarType Car;
        public PlayerRaceData RaceData;
        public PlayerState State;
        public bool EngineRunning;
        public bool Braking;
        public bool Horning;
        public bool Backfiring;
    }

    internal sealed class PacketPlayerBumped
    {
        public uint PlayerId;
        public byte PlayerNumber;
        public int BumpX;
        public int BumpY;
        public ushort BumpSpeed;
    }

    internal sealed class PacketLoadCustomTrack
    {
        public byte NrOfLaps;
        public string TrackName = string.Empty;
        public byte TrackWeather;
        public byte TrackAmbience;
        public ushort TrackLength;
        public MultiplayerDefinition[] Definitions = Array.Empty<MultiplayerDefinition>();
    }

    internal sealed class PacketRaceResults
    {
        public byte NPlayers;
        public byte[] Results = Array.Empty<byte>();
    }
}
