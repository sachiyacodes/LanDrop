// Models/AppSettings.cs
namespace LanDrop.Models
{
    public class AppSettings
    {
        public int    TransferPort     { get; set; } = 55_001;
        public int    DiscoveryPort    { get; set; } = 55_002;
        public string ReceiveSavePath  { get; set; } =
            System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                "Downloads", "LanDrop");
        public string? PinCode         { get; set; } = null;
        // Performance: larger buffers = faster LAN transfer
        public int    SocketBufferSize { get; set; } = 4 * 1024 * 1024;   // 4 MB
        public int    ChunkSize        { get; set; } = 4 * 1024 * 1024;   // 4 MB
        public bool   DarkMode         { get; set; } = true;
        public bool   AutoAccept       { get; set; } = false;
        public int    MaxRetries       { get; set; } = 3;
        public bool   MinimizeToTray   { get; set; } = true;
        // WiFi Direct
        public string WifiDirectSsid   { get; set; } = "LanDrop-Direct";
        public string WifiDirectPass   { get; set; } = "11111111";
    }
}
