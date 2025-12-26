namespace TopSpeed.Server.Network
{
    internal readonly struct ServerSnapshot
    {
        public ServerSnapshot(string name, int port, int maxPlayers, int playerCount, bool raceStarted, bool trackSelected, string trackName)
        {
            Name = name;
            Port = port;
            MaxPlayers = maxPlayers;
            PlayerCount = playerCount;
            RaceStarted = raceStarted;
            TrackSelected = trackSelected;
            TrackName = trackName;
        }

        public string Name { get; }
        public int Port { get; }
        public int MaxPlayers { get; }
        public int PlayerCount { get; }
        public bool RaceStarted { get; }
        public bool TrackSelected { get; }
        public string TrackName { get; }
    }
}
