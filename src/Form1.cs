namespace SolarMonitorBrightness;

public partial class Form1 : Form
{
    private const string AppTitle = "DDC/CI HA-Bridge";
    private const string AppVersion = "1.4";

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
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _notifyIcon = new NotifyIcon(components)
        {
            Icon = _appIcon,
            Text = AppTitle,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();

        _mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14)
        };
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _mainLayout.Controls.Add(BuildTabs(), 0, 0);
        _mainLayout.Controls.Add(BuildButtonRow(), 0, 1);
        Controls.Add(_mainLayout);
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

        var settingsPage = new TabPage("Settings") { Padding = new Padding(10) };
        settingsPage.Controls.Add(BuildSettingsPanel());

        var aboutPage = new TabPage("About") { Padding = new Padding(10) };
        aboutPage.Controls.Add(BuildAboutPanel());

        tabs.TabPages.Add(statusPage);
        tabs.TabPages.Add(settingsPage);
        tabs.TabPages.Add(aboutPage);
        tabs.SelectedIndex = 0;
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

    private Control BuildSettingsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var homeAssistantButton = new Button { Text = "Home Assistant...", AutoSize = true };
        homeAssistantButton.Click += (_, _) => ShowHomeAssistantSettings();

        var brightnessButton = new Button { Text = "Brightness control...", AutoSize = true };
        brightnessButton.Click += (_, _) => ShowBrightnessSettings();

        panel.Controls.Add(homeAssistantButton);
        panel.Controls.Add(brightnessButton);
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
        var availableMonitors = new List<DetectedMonitor>();
        try
        {
            availableMonitors = MonitorBrightnessController.GetMonitors();
        }
        catch
        {
            availableMonitors = [];
        }

        var defaultCurve = _settings.DefaultCurve.Clone();
        var monitorCurves = _settings.MonitorCurves.Select(curve => curve.Clone()).ToList();
        var referenceLux = _settings.ReferenceLux;
        var selectedKey = "";
        var loading = false;

        using var dialog = new Form
        {
            Text = "Brightness control settings",
            Icon = _appIcon,
            ClientSize = new Size(760, 620),
            MinimumSize = new Size(760, 660),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var enabledBox = new CheckBox { Text = "Enable automatic brightness control", Checked = _settings.Enabled, AutoSize = true };
        var startWithWindowsBox = new CheckBox { Text = "Start with Windows", Checked = IsStartupEnabled(), AutoSize = true };
        var startMinimizedBox = new CheckBox { Text = "Start minimized to tray", Checked = _settings.StartMinimized, AutoSize = true };
        var pollingSecondsBox = CreateNumberBox(5, 3600, 1, Math.Clamp(_settings.PollingSeconds, 5, 3600));
        var referenceLuxBox = CreateNumberBox(1, 1000000, 1000, referenceLux);
        var monitorBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 };
        var customCurveBox = new CheckBox { Text = "Use custom graph for this monitor", AutoSize = true };
        var editor = new CurveEditorControl { Size = new Size(700, 320), Anchor = AnchorStyles.Left | AnchorStyles.Right, ReferenceLux = referenceLux };
        var selectedLuxBox = CreateNumberBox(0, referenceLux, 100, 0);
        var selectedBrightnessBox = CreateNumberBox(1, 100, 1, 1);
        var addPointButton = new Button { Text = "Add point", AutoSize = true };
        var removePointButton = new Button { Text = "Remove point", AutoSize = true };
        var resetGraphButton = new Button { Text = "Reset graph", AutoSize = true };

        monitorBox.Items.Add(new MonitorSelectionItem("Default graph", "", isDefault: true));
        foreach (var monitor in availableMonitors)
        {
            monitorBox.Items.Add(new MonitorSelectionItem(monitor.DisplayName, monitor.Key, isDefault: false));
        }
        monitorBox.SelectedIndex = 0;

        var optionsGrid = CreateTwoColumnGrid(205);
        optionsGrid.Dock = DockStyle.Top;
        optionsGrid.Padding = new Padding(14, 14, 14, 0);
        AddRow(optionsGrid, "Polling interval (seconds)", pollingSecondsBox);
        AddRow(optionsGrid, "Reference lux", referenceLuxBox);
        AddRow(optionsGrid, "", enabledBox);
        AddRow(optionsGrid, "", startWithWindowsBox);
        AddRow(optionsGrid, "", startMinimizedBox);
        AddRow(optionsGrid, "Graph target", monitorBox);
        AddRow(optionsGrid, "", customCurveBox);

        var pointGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 7,
            Padding = new Padding(14, 0, 14, 0)
        };
        pointGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pointGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        pointGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pointGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        pointGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pointGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pointGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pointGrid.Controls.Add(new Label { Text = "Selected lux", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        pointGrid.Controls.Add(selectedLuxBox, 1, 0);
        pointGrid.Controls.Add(new Label { Text = "Brightness (%)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 6, 6, 6) }, 2, 0);
        pointGrid.Controls.Add(selectedBrightnessBox, 3, 0);
        pointGrid.Controls.Add(addPointButton, 4, 0);
        pointGrid.Controls.Add(removePointButton, 5, 0);
        pointGrid.Controls.Add(resetGraphButton, 6, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1
        };
        body.Controls.Add(optionsGrid);
        body.Controls.Add(editor);
        body.Controls.Add(pointGrid);

        MonitorCurveSettings GetOrCreateMonitorCurve(string monitorKey)
        {
            var monitor = availableMonitors.FirstOrDefault(item => item.Key == monitorKey);
            var monitorCurve = monitorCurves.FirstOrDefault(item => item.MonitorKey == monitorKey);
            if (monitorCurve is not null)
            {
                if (monitor is not null)
                {
                    monitorCurve.DisplayName = monitor.DisplayName;
                }

                return monitorCurve;
            }

            monitorCurve = new MonitorCurveSettings
            {
                MonitorKey = monitorKey,
                DisplayName = monitor?.DisplayName ?? monitorKey,
                Enabled = false,
                Curve = defaultCurve.Clone()
            };
            monitorCurves.Add(monitorCurve);
            return monitorCurve;
        }

        void SaveEditorToSelected()
        {
            if (loading)
            {
                return;
            }

            if (selectedKey.Length == 0)
            {
                defaultCurve = editor.ToCurve();
                return;
            }

            var monitorCurve = GetOrCreateMonitorCurve(selectedKey);
            if (customCurveBox.Checked)
            {
                monitorCurve.Enabled = true;
                monitorCurve.Curve = editor.ToCurve();
            }
        }

        void LoadSelectedGraph()
        {
            loading = true;
            if (monitorBox.SelectedItem is not MonitorSelectionItem item || item.IsDefault)
            {
                selectedKey = "";
                customCurveBox.Checked = false;
                customCurveBox.Enabled = false;
                editor.Enabled = true;
                editor.SetPoints(defaultCurve.Points);
            }
            else
            {
                selectedKey = item.MonitorKey;
                customCurveBox.Enabled = true;
                var monitorCurve = GetOrCreateMonitorCurve(selectedKey);
                customCurveBox.Checked = monitorCurve.Enabled;
                editor.Enabled = monitorCurve.Enabled;
                editor.SetPoints(monitorCurve.Enabled ? monitorCurve.Curve.Points : defaultCurve.Points);
            }

            loading = false;
        }

        void UpdateSelectedPointFields()
        {
            loading = true;
            if (editor.SelectedPoint is { } point)
            {
                selectedLuxBox.Value = Math.Clamp(editor.GetSelectedEffectiveLux(), selectedLuxBox.Minimum, selectedLuxBox.Maximum);
                selectedBrightnessBox.Value = Math.Clamp(point.Brightness, selectedBrightnessBox.Minimum, selectedBrightnessBox.Maximum);
            }

            loading = false;
        }

        monitorBox.SelectedIndexChanged += (_, _) =>
        {
            SaveEditorToSelected();
            LoadSelectedGraph();
            UpdateSelectedPointFields();
        };
        referenceLuxBox.ValueChanged += (_, _) =>
        {
            if (loading)
            {
                return;
            }

            SaveEditorToSelected();
            loading = true;
            referenceLux = referenceLuxBox.Value;
            SetNumericMaximumSafely(selectedLuxBox, referenceLux);
            editor.ReferenceLux = referenceLux;
            loading = false;
            UpdateSelectedPointFields();
        };
        referenceLuxBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            referenceLux = referenceLuxBox.Value;
            editor.ReferenceLux = referenceLux;
            SetNumericMaximumSafely(selectedLuxBox, referenceLux);
            UpdateSelectedPointFields();
            e.SuppressKeyPress = true;
            e.Handled = true;
        };
        customCurveBox.CheckedChanged += (_, _) =>
        {
            if (loading || selectedKey.Length == 0)
            {
                return;
            }

            var monitorCurve = GetOrCreateMonitorCurve(selectedKey);
            monitorCurve.Enabled = customCurveBox.Checked;
            editor.Enabled = customCurveBox.Checked;
            editor.SetPoints(customCurveBox.Checked ? monitorCurve.Curve.Points : defaultCurve.Points);
            UpdateSelectedPointFields();
        };
        editor.PointsChanged += (_, _) => SaveEditorToSelected();
        editor.SelectedPointChanged += (_, _) => UpdateSelectedPointFields();
        selectedLuxBox.ValueChanged += (_, _) =>
        {
            if (!loading && editor.Enabled)
            {
                editor.UpdateSelected(selectedLuxBox.Value, (int)selectedBrightnessBox.Value);
            }
        };
        selectedBrightnessBox.ValueChanged += (_, _) =>
        {
            if (!loading && editor.Enabled)
            {
                editor.UpdateSelected(selectedLuxBox.Value, (int)selectedBrightnessBox.Value);
            }
        };
        addPointButton.Click += (_, _) => editor.AddPoint();
        removePointButton.Click += (_, _) => editor.RemoveSelectedPoint();
        resetGraphButton.Click += (_, _) =>
        {
            var reset = selectedKey.Length == 0 ? BrightnessCurve.CreateDefault() : defaultCurve.Clone();
            editor.SetPoints(reset.Points);
            SaveEditorToSelected();
        };

        LoadSelectedGraph();
        UpdateSelectedPointFields();

        var buttons = BuildDialogButtons(dialog, () =>
        {
            SaveEditorToSelected();
            _settings.PollingSeconds = (int)pollingSecondsBox.Value;
            _settings.Enabled = enabledBox.Checked;
            _settings.StartMinimized = startMinimizedBox.Checked;
            _settings.ReferenceLux = referenceLuxBox.Value;
            _settings.DefaultCurve = defaultCurve;
            _settings.MonitorCurves = monitorCurves;

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
        dialog.Controls.Add(body);
        FitDialogToContent(dialog, body, buttons, 760);

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

    private static void SetNumericMaximumSafely(NumericUpDown numericBox, decimal maximum)
    {
        maximum = Math.Max(numericBox.Minimum, maximum);
        if (numericBox.Value > maximum)
        {
            numericBox.Value = maximum;
        }

        numericBox.Maximum = maximum;
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
        LockWindowSize(clientSize);
    }

    private void EnsureWindowFitsContent()
    {
        PerformLayout();
        var preferredContentSize = _mainLayout.GetPreferredSize(new Size(ClientSize.Width, 0));
        var clientSize = new Size(
            Math.Max(500, preferredContentSize.Width),
            preferredContentSize.Height);

        if (ClientSize.Width < clientSize.Width || ClientSize.Height < clientSize.Height)
        {
            MaximumSize = Size.Empty;
            ClientSize = clientSize;
        }
        LockWindowSize(ClientSize);
    }

    private void LockWindowSize(Size clientSize)
    {
        var fixedSize = SizeFromClientSize(clientSize);
        MinimumSize = fixedSize;
        MaximumSize = fixedSize;
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

            var previewBrightness = BrightnessMapper.MapLuxToBrightness(lux, _settings.DefaultCurve, _settings.ReferenceLux);
            _luxStatusLabel.Text = $"{lux:N0} lux";
            _brightnessStatusLabel.Text = $"{previewBrightness}%";
            _messageStatusLabel.Text = "Applying monitor brightness...";

            var result = await Task.Run(() => MonitorBrightnessController.SetBrightnessForAllMonitors(monitor =>
            {
                var curve = GetEffectiveCurve(monitor);
                return BrightnessMapper.MapLuxToBrightness(lux, curve, _settings.ReferenceLux);
            }));
            _brightnessStatusLabel.Text = FormatBrightnessValues(result.AppliedBrightnessValues, previewBrightness);
            _monitorStatusLabel.Text = $"{result.Changed} updated, {result.Failed} unavailable";
            _messageStatusLabel.Text = $"Last updated at {DateTime.Now:T}";
            UpdateTrayText($"{_brightnessStatusLabel.Text} at {lux:N0} lux");
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

    private BrightnessCurve GetEffectiveCurve(DetectedMonitor monitor)
    {
        var monitorCurve = _settings.MonitorCurves.FirstOrDefault(curve =>
            curve.Enabled &&
            curve.MonitorKey.Equals(monitor.Key, StringComparison.OrdinalIgnoreCase));

        if (monitorCurve is null && !string.IsNullOrWhiteSpace(monitor.Description))
        {
            monitorCurve = _settings.MonitorCurves.FirstOrDefault(curve =>
                curve.Enabled &&
                !string.IsNullOrWhiteSpace(curve.DisplayName) &&
                curve.DisplayName.StartsWith(monitor.Description, StringComparison.OrdinalIgnoreCase));
        }

        return monitorCurve?.Curve ?? _settings.DefaultCurve;
    }

    private static string FormatBrightnessValues(IReadOnlyCollection<int> values, int fallback)
    {
        if (values.Count == 0)
        {
            return $"{fallback}%";
        }

        var distinct = values.Distinct().Order().ToList();
        if (distinct.Count == 1)
        {
            return $"{distinct[0]}%";
        }

        return $"{distinct[0]}-{distinct[^1]}% per monitor";
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

    private sealed class MonitorSelectionItem(string text, string monitorKey, bool isDefault)
    {
        public string MonitorKey { get; } = monitorKey;
        public bool IsDefault { get; } = isDefault;

        public override string ToString() => text;
    }
}
