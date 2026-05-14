using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SolarMonitorBrightness;

internal static class MonitorBrightnessController
{
    public static List<DetectedMonitor> GetMonitors()
    {
        var monitors = new List<DetectedMonitor>();
        EnumeratePhysicalMonitors((monitor, _) => monitors.Add(monitor));
        return monitors;
    }

    public static MonitorBrightnessResult SetBrightnessForAllMonitors(Func<DetectedMonitor, int> brightnessSelector)
    {
        var result = new MonitorBrightnessResult();
        EnumeratePhysicalMonitors((monitor, handle) =>
        {
            var brightness = Math.Clamp(brightnessSelector(monitor), 1, 100);
            if (SetMonitorBrightness(handle, (uint)brightness))
            {
                result.Changed++;
                result.AppliedBrightnessValues.Add(brightness);
            }
            else
            {
                result.Failed++;
            }
        });

        return result;
    }

    private static void EnumeratePhysicalMonitors(Action<DetectedMonitor, IntPtr> action)
    {
        var displayMonitors = new List<IntPtr>();

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect monitorBounds, IntPtr data) =>
            {
                displayMonitors.Add(hMonitor);
                return true;
            }, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Monitors could not be enumerated.");
        }

        var globalIndex = 0;
        foreach (var displayMonitor in displayMonitors)
        {
            var deviceName = GetDisplayDeviceName(displayMonitor);
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(displayMonitor, out var count) || count == 0)
            {
                continue;
            }

            var physicalMonitors = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(displayMonitor, count, physicalMonitors))
            {
                continue;
            }

            try
            {
                for (var index = 0; index < physicalMonitors.Length; index++)
                {
                    var description = physicalMonitors[index].Description?.Trim();
                    if (string.IsNullOrWhiteSpace(description))
                    {
                        description = "DDC/CI monitor";
                    }

                    var key = $"{deviceName}|{description}|{index}";
                    var displayName = $"{description} ({deviceName})";
                    if (string.IsNullOrWhiteSpace(deviceName))
                    {
                        key = $"{description}|{globalIndex}";
                        displayName = description;
                    }

                    action(new DetectedMonitor
                    {
                        Key = key,
                        DisplayName = displayName,
                        DeviceName = deviceName,
                        Description = description
                    }, physicalMonitors[index].Handle);
                    globalIndex++;
                }
            }
            finally
            {
                DestroyPhysicalMonitors(count, physicalMonitors);
            }
        }
    }

    private static string GetDisplayDeviceName(IntPtr monitor)
    {
        var info = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
            DeviceName = new string('\0', 32)
        };

        return GetMonitorInfo(monitor, ref info) ? info.DeviceName.TrimEnd('\0') : "";
    }

    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumDelegate lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor,
        uint dwPhysicalMonitorArraySize,
        [Out] PhysicalMonitor[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(
        uint dwPhysicalMonitorArraySize,
        [In] PhysicalMonitor[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }
}

internal sealed class DetectedMonitor
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Description { get; set; } = "";
}

internal sealed class MonitorBrightnessResult
{
    public int Changed { get; set; }
    public int Failed { get; set; }
    public List<int> AppliedBrightnessValues { get; } = [];
}
