namespace TopSpeed.Server.Config
{
    internal sealed class ServerSettings
    {
        public int Port { get; set; } = 28630;
        public int DiscoveryPort { get; set; } = 28631;
        public int MaxPlayers { get; set; } = 8;
        public int ServerNumber { get; set; } = 1000;
        public string Name { get; set; } = "TopSpeed Server";
        public string Motd { get; set; } = string.Empty;
        public bool EnableDiscovery { get; set; } = true;
    }
}
