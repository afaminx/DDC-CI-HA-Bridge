namespace SolarMonitorBrightness;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 9F));

        using var legacyMutex = new Mutex(true, "SolarMonitorBrightness.SingleInstance", out var legacyCreated);
        using var mutex = new Mutex(true, @"Local\DDC-CI-HA-Bridge.SingleInstance", out var createdNew);
        if (!legacyCreated || !createdNew)
        {
            return;
        }

        Application.Run(new Form1(args.Contains("--minimized", StringComparer.OrdinalIgnoreCase)));
    }
}
