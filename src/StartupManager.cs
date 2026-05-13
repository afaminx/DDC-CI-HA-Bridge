using Microsoft.Win32;

namespace SolarMonitorBrightness;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DDC/CI HA-Bridge";
    private const string LegacyValueName = "SolarMonitorBrightness";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return HasCurrentExecutable(key?.GetValue(ValueName)) ||
               HasCurrentExecutable(key?.GetValue(LegacyValueName));
    }

    public static void SetEnabled(bool enabled, bool startMinimized)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ??
                        Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            key.SetValue(ValueName, GetStartupCommand(startMinimized));
            key.DeleteValue(LegacyValueName, false);
        }
        else
        {
            key.DeleteValue(ValueName, false);
            key.DeleteValue(LegacyValueName, false);
        }
    }

    private static bool HasCurrentExecutable(object? value)
    {
        var executablePath = GetExecutablePath();
        return value is string text &&
               text.Contains(executablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStartupCommand(bool startMinimized)
    {
        var argument = startMinimized ? " --minimized" : "";
        return $"\"{GetExecutablePath()}\"{argument}";
    }

    private static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        return Application.ExecutablePath;
    }
}
