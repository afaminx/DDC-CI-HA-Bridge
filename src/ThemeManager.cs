using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SolarMonitorBrightness;

internal static class ThemeManager
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(Form form, params ContextMenuStrip[] menus)
    {
        var palette = Palette.FromSystem();
        ApplyTitleBar(form, palette.Dark);
        ApplyControl(form, palette);

        foreach (var menu in menus)
        {
            ApplyMenu(menu, palette);
        }
    }

    public static void ApplyControlTree(Control control)
    {
        ApplyControl(control, Palette.FromSystem());
    }

    private static void ApplyControl(Control control, Palette palette)
    {
        switch (control)
        {
            case CurveEditorControl curveEditor:
                curveEditor.ApplyTheme(palette.Dark);
                break;
            case TableLayoutPanel:
            case FlowLayoutPanel:
            case TabPage:
            case Form:
            case UserControl:
            case Panel:
            case TabControl:
            case GroupBox:
                control.BackColor = palette.Background;
                control.ForeColor = palette.Text;
                break;
            case TextBoxBase:
                control.BackColor = palette.InputBackground;
                control.ForeColor = palette.Text;
                if (palette.Dark && control is TextBoxBase textBox)
                {
                    textBox.BorderStyle = BorderStyle.None;
                }
                break;
            case NumericUpDown:
                control.BackColor = palette.InputBackground;
                control.ForeColor = palette.Text;
                if (palette.Dark && control is NumericUpDown numeric)
                {
                    numeric.BorderStyle = BorderStyle.None;
                }
                break;
            case ComboBox comboBox:
                comboBox.FlatStyle = palette.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                comboBox.BackColor = palette.InputBackground;
                comboBox.ForeColor = palette.Text;
                break;
            case Button button:
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = palette.Dark ? FlatStyle.Flat : FlatStyle.Standard;
                button.BackColor = palette.ButtonBackground;
                button.ForeColor = palette.Text;
                button.FlatAppearance.BorderColor = palette.Dark ? Color.FromArgb(70, 70, 70) : SystemColors.ControlDark;
                button.FlatAppearance.MouseOverBackColor = palette.Dark ? Color.FromArgb(70, 70, 70) : SystemColors.ControlLight;
                button.FlatAppearance.MouseDownBackColor = palette.Dark ? Color.FromArgb(82, 82, 82) : SystemColors.ControlLightLight;
                break;
            case CheckBox checkBox:
                checkBox.BackColor = palette.Background;
                checkBox.ForeColor = palette.Text;
                checkBox.UseVisualStyleBackColor = false;
                break;
            case LinkLabel linkLabel:
                linkLabel.BackColor = palette.Background;
                linkLabel.ForeColor = palette.Text;
                linkLabel.LinkColor = palette.Link;
                linkLabel.ActiveLinkColor = palette.Accent;
                linkLabel.VisitedLinkColor = palette.Link;
                break;
            case Label:
                control.BackColor = palette.Background;
                control.ForeColor = palette.Text;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyControl(child, palette);
        }
    }

    private static void ApplyMenu(ContextMenuStrip menu, Palette palette)
    {
        menu.BackColor = palette.Background;
        menu.ForeColor = palette.Text;
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = palette.Background;
            item.ForeColor = palette.Text;
        }
    }

    private static void ApplyTitleBar(Form form, bool dark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private sealed record Palette(bool Dark, Color Background, Color InputBackground, Color ButtonBackground, Color Text, Color Link, Color Accent)
    {
        public static Palette FromSystem()
        {
            return IsDarkMode()
                ? new Palette(true, Color.FromArgb(32, 32, 32), Color.FromArgb(45, 45, 45), Color.FromArgb(55, 55, 55), Color.FromArgb(242, 242, 242), Color.FromArgb(99, 168, 255), Color.FromArgb(0, 120, 215))
                : new Palette(false, SystemColors.Control, Color.White, SystemColors.Control, SystemColors.ControlText, Color.FromArgb(0, 102, 204), Color.FromArgb(0, 120, 215));
        }
    }
}
