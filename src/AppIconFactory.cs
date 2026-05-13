using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SolarMonitorBrightness;

internal static class AppIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var background = new LinearGradientBrush(
            new Rectangle(0, 0, 32, 32),
            Color.FromArgb(31, 111, 235),
            Color.FromArgb(18, 61, 124),
            45f);
        graphics.FillRoundedRectangle(background, new RectangleF(2, 2, 28, 28), 6);

        using var monitorBrush = new SolidBrush(Color.White);
        graphics.FillRoundedRectangle(monitorBrush, new RectangleF(7, 8, 18, 12), 2);
        graphics.FillRectangle(monitorBrush, new RectangleF(14, 20, 4, 4));
        graphics.FillRoundedRectangle(monitorBrush, new RectangleF(10, 24, 12, 2), 1);

        using var accent = new SolidBrush(Color.FromArgb(120, 220, 255));
        graphics.FillEllipse(accent, 20, 6, 6, 6);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
