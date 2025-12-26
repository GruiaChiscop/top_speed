namespace TopSpeed.Network
{
    internal static class ClientProtocol
    {
        public const byte Version = 0x1E;
        public const int DefaultServerPort = 28630;
        public const int DefaultDiscoveryPort = 28631;
        public const int MaxPlayerNameLength = 24;
        public const int MaxMotdLength = 128;
    }

    internal enum ClientCommand : byte
    {
        Disconnect = 0,
        PlayerNumber = 1,
        PlayerData = 2,
        PlayerState = 3,
        StartRace = 4,
        StopRace = 5,
        RaceAborted = 6,
        PlayerDataToServer = 7,
        PlayerFinished = 8,
        PlayerFinalize = 9,
        PlayerStarted = 10,
        PlayerCrashed = 11,
        PlayerBumped = 12,
        PlayerDisconnected = 13,
        LoadCustomTrack = 14,
        PlayerHello = 15,
        ServerInfo = 16,
        KeepAlive = 17
    }

    internal enum ClientPlayerState : byte
    {
        Undefined = 0,
        NotReady = 1,
        AwaitingStart = 2,
        Racing = 3,
        Finished = 4
    }
}
