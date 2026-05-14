namespace SolarMonitorBrightness;

internal sealed class ThemedComboBox : ComboBox
{
    public ThemedComboBox()
    {
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();
        var dark = ThemeManager.IsDarkMode();
        var backColor = dark ? Color.FromArgb(45, 45, 45) : BackColor;
        var textColor = dark ? Color.FromArgb(242, 242, 242) : ForeColor;

        using var background = new SolidBrush(backColor);
        using var textBrush = new SolidBrush(textColor);
        e.Graphics.FillRectangle(background, e.Bounds);

        if (e.Index >= 0 && e.Index < Items.Count)
        {
            TextRenderer.DrawText(
                e.Graphics,
                Items[e.Index]?.ToString() ?? "",
                Font,
                e.Bounds,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        const int wmPaint = 0x000F;
        if (message.Msg == wmPaint && ThemeManager.IsDarkMode() && DropDownStyle == ComboBoxStyle.DropDownList)
        {
            using var graphics = CreateGraphics();
            DrawCollapsed(graphics);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        DropDownWidth = Math.Max(Width, 1);
        Invalidate();
    }

    protected override void OnDropDown(EventArgs e)
    {
        DropDownWidth = Math.Max(Width, 1);
        base.OnDropDown(e);
    }

    private void DrawCollapsed(Graphics graphics)
    {
        var bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var backgroundColor = Color.FromArgb(45, 45, 45);
        var borderColor = Color.FromArgb(70, 70, 70);
        var textColor = Color.FromArgb(242, 242, 242);
        var arrowArea = new Rectangle(bounds.Right - 24, bounds.Top, 24, bounds.Height);
        var textArea = new Rectangle(bounds.Left + 4, bounds.Top, bounds.Width - arrowArea.Width - 8, bounds.Height);

        using var background = new SolidBrush(backgroundColor);
        using var border = new Pen(borderColor);
        using var arrowBrush = new SolidBrush(textColor);
        graphics.FillRectangle(background, bounds);
        graphics.DrawRectangle(border, new Rectangle(bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1));

        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            textArea,
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var centerX = arrowArea.Left + arrowArea.Width / 2;
        var centerY = arrowArea.Top + arrowArea.Height / 2;
        var triangle = new[]
        {
            new Point(centerX - 4, centerY - 2),
            new Point(centerX + 4, centerY - 2),
            new Point(centerX, centerY + 3)
        };
        graphics.FillPolygon(arrowBrush, triangle);
    }
}
