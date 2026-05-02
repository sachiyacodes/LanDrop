// Services/SettingsService.cs
// Persists AppSettings to/from JSON in %AppData%\LanDrop

using System;
using System.IO;
using System.Text.Json;
using LanDrop.Models;
using Microsoft.Extensions.Logging;

namespace LanDrop.Services
{
    public class SettingsService
    {
        private static readonly string ConfigDir  =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanDrop");
        private static readonly string ConfigFile =
            Path.Combine(ConfigDir, "settings.json");

        private readonly ILogger<SettingsService> _logger;
        private static readonly JsonSerializerOptions _opts =
            new() { WriteIndented = true };

        public SettingsService(ILogger<SettingsService> logger) => _logger = logger;

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var json = File.ReadAllText(ConfigFile);
                    var s    = JsonSerializer.Deserialize<AppSettings>(json, _opts);
                    if (s is not null)
                    {
                        _logger.LogInformation("Settings loaded from {Path}.", ConfigFile);
                        return s;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load settings; using defaults.");
            }
            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigFile, JsonSerializer.Serialize(settings, _opts));
                _logger.LogInformation("Settings saved.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not save settings.");
            }
        }
    }
}
