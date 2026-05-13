using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolarMonitorBrightness;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int SettingsVersion { get; set; } = 2;
    public string HomeAssistantAddress { get; set; } = "192.168.3.134:8123";
    public string SensorEntityId { get; set; } = "sensor.gw2000a_solar_lux";
    [JsonIgnore]
    public string Token { get; set; } = "";
    public string ProtectedToken { get; set; } = "";
    public decimal LuxAtMinimumBrightness { get; set; } = 0;
    public decimal LuxAtMaximumBrightness { get; set; } = 40000;
    public int MinimumMonitorBrightness { get; set; } = 1;
    public int MaximumMonitorBrightness { get; set; } = 100;
    public int PollingSeconds { get; set; } = 5;
    public bool Enabled { get; set; } = true;
    public bool StartMinimized { get; set; } = true;

    public static string SettingsPath
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SolarMonitorBrightness");
            return Path.Combine(folder, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.Token = TokenProtector.Unprotect(settings.ProtectedToken);
            settings.MigrateDefaults();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var folder = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        ProtectedToken = TokenProtector.Protect(Token);
        SettingsVersion = 2;
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private void MigrateDefaults()
    {
        if (SettingsVersion >= 2)
        {
            return;
        }

        if (LuxAtMinimumBrightness == 100000 &&
            LuxAtMaximumBrightness == 0 &&
            MinimumMonitorBrightness == 10 &&
            MaximumMonitorBrightness == 100 &&
            PollingSeconds == 30)
        {
            LuxAtMinimumBrightness = 0;
            LuxAtMaximumBrightness = 40000;
            MinimumMonitorBrightness = 1;
            MaximumMonitorBrightness = 100;
            PollingSeconds = 5;
        }

        SettingsVersion = 2;
    }
}
