namespace SolarMonitorBrightness;

public partial class Form1 : Form
{
    private const string AppTitle = "DDC/CI HA-Bridge";
    private const string AppVersion = "1.2";

    private readonly bool _startHidden;
    private readonly HomeAssistantClient _homeAssistant = new();
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly Icon _appIcon = AppIconFactory.Create();

    private AppSettings _settings = AppSettings.Load();
    private NotifyIcon _notifyIcon = null!;
    private bool _exitRequested;

    private readonly Label _luxStatusLabel = new();
    private readonly Label _brightnessStatusLabel = new();
    private readonly Label _monitorStatusLabel = new();
    private readonly Label _messageStatusLabel = new();
    private readonly Button _toggleButton = new();
    private TableLayoutPanel _mainLayout = null!;

    public Form1(bool startHidden)
    {
        _startHidden = startHidden;
        InitializeComponent();
        BuildUserInterface();

        _pollTimer.Tick += async (_, _) => await PollAndApplyAsync();
        Load += (_, _) => RestartTimer();
        Shown += (_, _) =>
        {
            if (_startHidden)
            {
                HideToTray();
            }
        };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray();
            }
        };
        FormClosing += HandleFormClosing;
        FormClosed += (_, _) =>
        {
            _pollTimer.Stop();
            _homeAssistant.Dispose();
            _pollLock.Dispose();
            _appIcon.Dispose();
        };
    }

    private void BuildUserInterface()
    {
        Text = AppTitle;
        Icon = _appIcon;
        ClientSize = new Size(500, 300);
        StartPosition = FormStartPosition.CenterScreen;

        _notifyIcon = new NotifyIcon(components)
        {
            Icon = _appIcon,
            Text = AppTitle,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();

        var menu = new MenuStrip();
        var settingsMenu = new ToolStripMenuItem("Settings");
        settingsMenu.DropDownItems.Add("Home Assistant...", null, (_, _) => ShowHomeAssistantSettings());
        settingsMenu.DropDownItems.Add("Brightness control...", null, (_, _) => ShowBrightnessSettings());
        menu.Items.Add(settingsMenu);
        MainMenuStrip = menu;

        _mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, menu.Height + 14, 14, 14)
        };
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = AppTitle,
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };

        _mainLayout.Controls.Add(title, 0, 0);
        _mainLayout.Controls.Add(BuildTabs(), 0, 1);
        _mainLayout.Controls.Add(BuildButtonRow(), 0, 2);
        Controls.Add(_mainLayout);
        Controls.Add(menu);
        menu.BringToFront();
        UpdateToggleButton();
        FitWindowToContent();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowFromTray());
        menu.Items.Add("Refresh now", null, async (_, _) => await PollAndApplyAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            _exitRequested = true;
            Close();
        });
        return menu;
    }

    private TabControl BuildTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Top,
            Size = new Size(472, 185),
            Margin = new Padding(0, 0, 0, 10)
        };

        var statusPage = new TabPage("Status") { Padding = new Padding(10) };
        statusPage.Controls.Add(BuildStatusPanel());

        var aboutPage = new TabPage("About") { Padding = new Padding(10) };
        aboutPage.Controls.Add(BuildAboutPanel());

        tabs.TabPages.Add(statusPage);
        tabs.TabPages.Add(aboutPage);
        return tabs;
    }

    private TableLayoutPanel BuildStatusPanel()
    {
        var grid = CreateTwoColumnGrid(170);
        grid.Dock = DockStyle.Fill;
        ConfigureStatusLabel(_luxStatusLabel);
        ConfigureStatusLabel(_brightnessStatusLabel);
        ConfigureStatusLabel(_monitorStatusLabel);
        ConfigureStatusLabel(_messageStatusLabel);

        AddRow(grid, "Current lux", _luxStatusLabel);
        AddRow(grid, "Target brightness", _brightnessStatusLabel);
        AddRow(grid, "Monitors", _monitorStatusLabel);
        AddRow(grid, "Message", _messageStatusLabel);

        return grid;
    }

    private Control BuildAboutPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1
        };

        panel.Controls.Add(new Label
        {
            Text = AppTitle,
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        });
        panel.Controls.Add(new Label { Text = $"Software version: {AppVersion}", AutoSize = true });
        panel.Controls.Add(new Label { Text = "License: MIT License", AutoSize = true });
        panel.Controls.Add(new Label { Text = "Copyright (c) 2026", AutoSize = true });

        var link = new LinkLabel
        {
            Text = "planetenexpress.de",
            AutoSize = true
        };
        link.LinkClicked += (_, _) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://planetenexpress.de",
                UseShellExecute = true
            });
        };
        panel.Controls.Add(link);

        return panel;
    }

    private FlowLayoutPanel BuildButtonRow()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false
        };

        var trayButton = new Button { Text = "Minimize to tray", AutoSize = true };
        trayButton.Click += (_, _) => HideToTray();

        var refreshButton = new Button { Text = "Refresh now", AutoSize = true };
        refreshButton.Click += async (_, _) => await PollAndApplyAsync();

        _toggleButton.AutoSize = true;
        _toggleButton.Click += (_, _) =>
        {
            _settings.Enabled = !_settings.Enabled;
            SaveSettings();
            RestartTimer();
        };

        row.Controls.Add(trayButton);
        row.Controls.Add(refreshButton);
        row.Controls.Add(_toggleButton);
        return row;
    }

    private void ShowHomeAssistantSettings()
    {
        using var dialog = new Form
        {
            Text = "Home Assistant settings",
            Icon = _appIcon,
            ClientSize = new Size(520, 215),
            MinimumSize = new Size(520, 255),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var addressBox = new TextBox { Text = _settings.HomeAssistantAddress, Dock = DockStyle.Fill };
        var sensorBox = new TextBox { Text = _settings.SensorEntityId, Dock = DockStyle.Fill };
        var tokenBox = new TextBox { Text = _settings.Token, Dock = DockStyle.Fill, UseSystemPasswordChar = true };

        var grid = CreateDialogGrid();
        AddRow(grid, "Home Assistant host (IP:port)", addressBox);
        AddRow(grid, "Sensor entity ID", sensorBox);
        AddRow(grid, "Long-lived access token", tokenBox);

        var buttons = BuildDialogButtons(dialog, () =>
        {
            _settings.HomeAssistantAddress = addressBox.Text.Trim();
            _settings.SensorEntityId = sensorBox.Text.Trim();
            _settings.Token = tokenBox.Text.Trim();
            SaveSettings();
            RestartTimer();
            return true;
        });
        dialog.Controls.Add(buttons);
        dialog.Controls.Add(grid);
        FitDialogToContent(dialog, grid, buttons, 520);

        dialog.ShowDialog(this);
    }

    private void ShowBrightnessSettings()
    {
        using var dialog = new Form
        {
            Text = "Brightness control settings",
            Icon = _appIcon,
            ClientSize = new Size(520, 355),
            MinimumSize = new Size(520, 395),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var enabledBox = new CheckBox { Text = "Enable automatic brightness control", Checked = _settings.Enabled, AutoSize = true };
        var startWithWindowsBox = new CheckBox { Text = "Start with Windows", Checked = IsStartupEnabled(), AutoSize = true };
        var startMinimizedBox = new CheckBox { Text = "Start minimized to tray", Checked = _settings.StartMinimized, AutoSize = true };
        var pollingSecondsBox = CreateNumberBox(5, 3600, 1, Math.Clamp(_settings.PollingSeconds, 5, 3600));
        var luxAtMinimumBox = CreateNumberBox(0, 10000000, 1000, _settings.LuxAtMinimumBrightness);
        var luxAtMaximumBox = CreateNumberBox(0, 10000000, 1000, _settings.LuxAtMaximumBrightness);
        var minimumBrightnessBox = CreateNumberBox(1, 100, 1, Math.Clamp(_settings.MinimumMonitorBrightness, 1, 100));
        var maximumBrightnessBox = CreateNumberBox(1, 100, 1, Math.Clamp(_settings.MaximumMonitorBrightness, 1, 100));

        var grid = CreateDialogGrid();
        AddRow(grid, "Polling interval (seconds)", pollingSecondsBox);
        AddRow(grid, "Lux at minimum brightness", luxAtMinimumBox);
        AddRow(grid, "Lux at maximum brightness", luxAtMaximumBox);
        AddRow(grid, "Minimum brightness (%)", minimumBrightnessBox);
        AddRow(grid, "Maximum brightness (%)", maximumBrightnessBox);
        AddRow(grid, "", enabledBox);
        AddRow(grid, "", startWithWindowsBox);
        AddRow(grid, "", startMinimizedBox);

        var buttons = BuildDialogButtons(dialog, () =>
        {
            _settings.PollingSeconds = (int)pollingSecondsBox.Value;
            _settings.LuxAtMinimumBrightness = luxAtMinimumBox.Value;
            _settings.LuxAtMaximumBrightness = luxAtMaximumBox.Value;
            _settings.MinimumMonitorBrightness = (int)minimumBrightnessBox.Value;
            _settings.MaximumMonitorBrightness = (int)maximumBrightnessBox.Value;
            _settings.Enabled = enabledBox.Checked;
            _settings.StartMinimized = startMinimizedBox.Checked;

            try
            {
                StartupManager.SetEnabled(startWithWindowsBox.Checked, startMinimizedBox.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Startup setting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            SaveSettings();
            RestartTimer();
            return true;
        });
        dialog.Controls.Add(buttons);
        dialog.Controls.Add(grid);
        FitDialogToContent(dialog, grid, buttons, 520);

        dialog.ShowDialog(this);
    }

    private FlowLayoutPanel BuildDialogButtons(Form dialog, Func<bool> save)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(10, 8, 10, 10),
            FlowDirection = FlowDirection.RightToLeft
        };

        var saveButton = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.None };
        saveButton.Click += (_, _) =>
        {
            if (!save())
            {
                return;
            }

            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };

        var cancelButton = new Button { Text = "Cancel", AutoSize = true };
        cancelButton.Click += (_, _) => dialog.Close();

        panel.Controls.Add(saveButton);
        panel.Controls.Add(cancelButton);
        dialog.AcceptButton = saveButton;
        dialog.CancelButton = cancelButton;
        return panel;
    }

    private static TableLayoutPanel CreateDialogGrid()
    {
        var grid = CreateTwoColumnGrid(205);
        grid.Dock = DockStyle.Top;
        grid.Padding = new Padding(14);
        return grid;
    }

    private static TableLayoutPanel CreateTwoColumnGrid(int labelWidth)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static void AddRow(TableLayoutPanel grid, string labelText, Control control)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 6)
        };

        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(0, 4, 0, 4);

        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private static NumericUpDown CreateNumberBox(decimal minimum, decimal maximum, decimal increment, decimal value)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            Value = ClampDecimal(value, minimum, maximum),
            DecimalPlaces = 0,
            ThousandsSeparator = true,
            Width = 145
        };
    }

    private static void ConfigureStatusLabel(Label label)
    {
        label.AutoSize = true;
        label.Text = "-";
        label.MaximumSize = new Size(270, 0);
    }

    private void FitWindowToContent()
    {
        PerformLayout();

        var preferredContentSize = _mainLayout.GetPreferredSize(new Size(500, 0));
        var clientSize = new Size(
            Math.Max(500, preferredContentSize.Width),
            preferredContentSize.Height);

        ClientSize = clientSize;
        MinimumSize = SizeFromClientSize(clientSize);
    }

    private void EnsureWindowFitsContent()
    {
        PerformLayout();
        var preferredContentSize = _mainLayout.GetPreferredSize(new Size(ClientSize.Width, 0));
        var clientSize = new Size(
            Math.Max(500, preferredContentSize.Width),
            preferredContentSize.Height);

        MinimumSize = SizeFromClientSize(clientSize);
        if (ClientSize.Width < clientSize.Width || ClientSize.Height < clientSize.Height)
        {
            ClientSize = clientSize;
        }
    }

    private static void FitDialogToContent(Form dialog, Control body, Control buttons, int minimumWidth)
    {
        dialog.PerformLayout();
        var bodySize = body.GetPreferredSize(new Size(minimumWidth, 0));
        var clientSize = new Size(
            Math.Max(minimumWidth, bodySize.Width),
            bodySize.Height + buttons.Height);

        dialog.ClientSize = clientSize;
        dialog.MinimumSize = dialog.Size;
    }

    private void SaveSettings() => _settings.Save();

    private bool IsStartupEnabled()
    {
        try
        {
            return StartupManager.IsEnabled();
        }
        catch (Exception ex)
        {
            _messageStatusLabel.Text = "Could not read startup setting: " + ex.Message;
            return false;
        }
    }

    private void RestartTimer()
    {
        _pollTimer.Stop();
        _pollTimer.Interval = Math.Clamp(_settings.PollingSeconds, 5, 3600) * 1000;

        if (_settings.Enabled)
        {
            _pollTimer.Start();
            _ = PollAndApplyAsync();
        }
        else
        {
            _messageStatusLabel.Text = "Automatic brightness control is paused.";
            _monitorStatusLabel.Text = "-";
            EnsureWindowFitsContent();
        }

        UpdateToggleButton();
    }

    private async Task PollAndApplyAsync()
    {
        if (!_settings.Enabled)
        {
            _messageStatusLabel.Text = "Automatic brightness control is paused.";
            EnsureWindowFitsContent();
            return;
        }

        if (!await _pollLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            _messageStatusLabel.Text = "Reading Home Assistant sensor...";
            var lux = await _homeAssistant.GetSensorLuxAsync(
                _settings.HomeAssistantAddress,
                _settings.SensorEntityId,
                _settings.Token,
                CancellationToken.None);

            var brightness = BrightnessMapper.MapLuxToBrightness(lux, _settings);
            _luxStatusLabel.Text = $"{lux:N0} lux";
            _brightnessStatusLabel.Text = $"{brightness}%";
            _messageStatusLabel.Text = "Applying monitor brightness...";

            var result = await Task.Run(() => MonitorBrightnessController.SetBrightnessForAllMonitors(brightness));
            _monitorStatusLabel.Text = $"{result.Changed} updated, {result.Failed} unavailable";
            _messageStatusLabel.Text = $"Last updated at {DateTime.Now:T}";
            UpdateTrayText($"{brightness}% at {lux:N0} lux");
        }
        catch (Exception ex)
        {
            _messageStatusLabel.Text = ex.Message;
            UpdateTrayText("Update failed");
        }
        finally
        {
            _pollLock.Release();
            EnsureWindowFitsContent();
        }
    }

    private void UpdateToggleButton()
    {
        _toggleButton.Text = _settings.Enabled ? "Pause" : "Resume";
    }

    private void UpdateTrayText(string suffix)
    {
        var text = AppTitle + " - " + suffix;
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exitRequested || e.CloseReason != CloseReason.UserClosing)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        if (value > maximum)
        {
            return maximum;
        }

        return value;
    }
}
