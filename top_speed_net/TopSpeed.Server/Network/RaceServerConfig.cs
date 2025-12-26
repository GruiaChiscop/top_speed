namespace TopSpeed.Server.Network
{
    internal sealed class RaceServerConfig
    {
        public int Port { get; set; } = 28630;
        public int DiscoveryPort { get; set; } = 28631;
        public int MaxPlayers { get; set; } = 8;
        public int ServerNumber { get; set; }
        public string? Name { get; set; }
        public bool EnableDiscovery { get; set; } = true;
        public string? Motd { get; set; }
    }
}
