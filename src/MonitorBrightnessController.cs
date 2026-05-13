using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SolarMonitorBrightness;

internal static class MonitorBrightnessController
{
    public static MonitorBrightnessResult SetBrightnessForAllMonitors(int brightness)
    {
        brightness = Math.Clamp(brightness, 1, 100);
        var result = new MonitorBrightnessResult();
        var monitors = new List<IntPtr>();

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect monitorBounds, IntPtr data) =>
            {
                monitors.Add(hMonitor);
                return true;
            }, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Monitors could not be enumerated.");
        }

        foreach (var monitor in monitors)
        {
            ApplyToMonitor(monitor, (uint)brightness, result);
        }

        return result;
    }

    private static void ApplyToMonitor(IntPtr monitor, uint brightness, MonitorBrightnessResult result)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
        {
            result.Failed++;
            return;
        }

        var physicalMonitors = new PhysicalMonitor[count];
        if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors))
        {
            result.Failed += (int)count;
            return;
        }

        try
        {
            foreach (var physicalMonitor in physicalMonitors)
            {
                if (SetMonitorBrightness(physicalMonitor.Handle, brightness))
                {
                    result.Changed++;
                }
                else
                {
                    result.Failed++;
                }
            }
        }
        finally
        {
            DestroyPhysicalMonitors(count, physicalMonitors);
        }
    }

    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumDelegate lpfnEnum,
        IntPtr dwData);

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
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }
}

internal sealed class MonitorBrightnessResult
{
    public int Changed { get; set; }
    public int Failed { get; set; }
}
