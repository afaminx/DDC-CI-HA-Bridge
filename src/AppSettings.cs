using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolarMonitorBrightness;

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int SettingsVersion { get; set; } = 4;
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
    public decimal ReferenceLux { get; set; } = 40000;
    public BrightnessCurve DefaultCurve { get; set; } = BrightnessCurve.CreateDefault();
    public List<MonitorCurveSettings> MonitorCurves { get; set; } = [];

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
        DefaultCurve.Normalize(ReferenceLux);
        ReferenceLux = Math.Clamp(ReferenceLux, 1, 1000000);
        foreach (var monitorCurve in MonitorCurves)
        {
            monitorCurve.Curve.Normalize(ReferenceLux);
        }

        SettingsVersion = 4;
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private void MigrateDefaults()
    {
        if (SettingsVersion < 2)
        {
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
        }

        if (SettingsVersion < 3 || DefaultCurve.Points.Count == 0)
        {
            DefaultCurve = BrightnessCurve.FromLegacy(
                LuxAtMinimumBrightness,
                MinimumMonitorBrightness,
                LuxAtMaximumBrightness,
                MaximumMonitorBrightness);
        }

        DefaultCurve.Normalize(ReferenceLux);
        ReferenceLux = Math.Clamp(ReferenceLux, 1, 1000000);
        foreach (var monitorCurve in MonitorCurves)
        {
            monitorCurve.Curve.Normalize(ReferenceLux);
        }

        SettingsVersion = 4;
    }
}

internal sealed class BrightnessCurve
{
    public List<CurvePoint> Points { get; set; } = [];

    public static BrightnessCurve CreateDefault()
    {
        return new BrightnessCurve
        {
            Points =
            [
                new CurvePoint { Lux = 0, Brightness = 1 },
                new CurvePoint { Lux = 100, Brightness = 100 }
            ]
        };
    }

    public static BrightnessCurve FromLegacy(decimal minLux, int minBrightness, decimal maxLux, int maxBrightness)
    {
        var curve = new BrightnessCurve
        {
            Points =
            [
                new CurvePoint { Lux = 0, Brightness = minBrightness },
                new CurvePoint { Lux = 100, Brightness = maxBrightness }
            ]
        };
        curve.Normalize();
        return curve;
    }

    public BrightnessCurve Clone()
    {
        return new BrightnessCurve
        {
            Points = Points.Select(point => point.Clone()).ToList()
        };
    }

    public void Normalize(decimal referenceLux = 40000)
    {
        referenceLux = Math.Clamp(referenceLux, 1, 1000000);
        var pointsLookAbsolute = Points.Any(point => point.Lux > 100);

        Points = Points
            .Select(point => new CurvePoint
            {
                Lux = Math.Clamp(pointsLookAbsolute ? point.Lux / referenceLux * 100 : point.Lux, 0, 100),
                Brightness = Math.Clamp(point.Brightness, 1, 100)
            })
            .GroupBy(point => point.Lux)
            .Select(group => group.Last())
            .OrderBy(point => point.Lux)
            .ToList();

        if (Points.Count == 0)
        {
            Points = CreateDefault().Points;
        }

        if (Points.Count == 1)
        {
            var only = Points[0];
            Points.Add(new CurvePoint
            {
                Lux = only.Lux == 0 ? 100 : 0,
                Brightness = only.Brightness
            });
            Points = Points.OrderBy(point => point.Lux).ToList();
        }
    }
}

internal sealed class CurvePoint
{
    public decimal Lux { get; set; }
    public int Brightness { get; set; }

    public CurvePoint Clone()
    {
        return new CurvePoint { Lux = Lux, Brightness = Brightness };
    }
}

internal sealed class MonitorCurveSettings
{
    public string MonitorKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    public BrightnessCurve Curve { get; set; } = BrightnessCurve.CreateDefault();

    public MonitorCurveSettings Clone()
    {
        return new MonitorCurveSettings
        {
            MonitorKey = MonitorKey,
            DisplayName = DisplayName,
            Enabled = Enabled,
            Curve = Curve.Clone()
        };
    }
}
