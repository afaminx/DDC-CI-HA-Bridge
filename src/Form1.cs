using Microsoft.Win32;

namespace SolarMonitorBrightness;

public partial class Form1 : Form
{
    private const string AppTitle = "DDC/CI HA-Bridge";
    internal const string AppVersion = "1.5";

    private readonly bool _startHidden;
    private readonly HomeAssistantClient _homeAssistant = new();
    private readonly GitHubUpdateChecker _updateChecker = new();
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly Icon _appIcon = AppIconFactory.Create();
    private readonly List<CurveEditorControl> _openCurveEditors = [];

    private AppSettings _settings = AppSettings.Load();
    private NotifyIcon _notifyIcon = null!;
    private bool _exitRequested;
    private bool _updatingSettingsControls;
    private decimal? _currentLux;

    private readonly Label _luxStatusLabel = new();
    private readonly Label _brightnessStatusLabel = new();
    private readonly Label _monitorStatusLabel = new();
    private readonly Label _messageStatusLabel = new();
    private readonly Button _toggleButton = new();
    private readonly CheckBox _enabledSettingsBox = new();
    private readonly CheckBox _startupSettingsBox = new();
    private readonly CheckBox _startMinimizedSettingsBox = new();
    private readonly CheckBox _checkUpdatesBox = new();
    private readonly List<Button> _tabButtons = [];
    private readonly List<Control> _tabPages = [];
    private Panel _tabContentPanel = null!;
    private int _selectedTabIndex;
    private TableLayoutPanel _mainLayout = null!;

    public Form1(bool startHidden)
    {
        _startHidden = startHidden;
        InitializeComponent();
        BuildUserInterface();

        _pollTimer.Tick += async (_, _) => await PollAndApplyAsync();
        Load += (_, _) =>
        {
            RestartTimer();
            _ = CheckForUpdatesOnStartupAsync();
        };
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
            _updateChecker.Dispose();
            _pollLock.Dispose();
            _appIcon.Dispose();
            SystemEvents.UserPreferenceChanged -= HandleUserPreferenceChanged;
        };
        SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;
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
        ApplyTheme();
        SyncSettingsControls();
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

    private void HandleUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            ApplyTheme();
        }
    }

    private void ApplyTheme()
    {
        if (_notifyIcon?.ContextMenuStrip is { } menu)
        {
            ThemeManager.Apply(this, menu);
        }
        else
        {
            ThemeManager.Apply(this);
        }

        Invalidate(true);
        StyleTabButtons();
    }

    private Control BuildTabs()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 10)
        };
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 620));

        var tabStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };

        _tabContentPanel = new Panel
        {
            Dock = DockStyle.Top,
            Size = new Size(840, 620),
            Padding = new Padding(10),
            Margin = new Padding(0)
        };

        AddTab(tabStrip, "Status", BuildStatusPanel());
        AddTab(tabStrip, "Settings", BuildSettingsPanel());
        AddTab(tabStrip, "About", BuildAboutPanel());

        host.Controls.Add(tabStrip, 0, 0);
        host.Controls.Add(_tabContentPanel, 0, 1);
        SelectTab(0);
        return host;
    }

    private void AddTab(FlowLayoutPanel tabStrip, string text, Control page)
    {
        var index = _tabPages.Count;
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 0, 4, 0),
            Padding = new Padding(10, 4, 10, 4)
        };
        button.Click += (_, _) => SelectTab(index);

        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _tabButtons.Add(button);
        _tabPages.Add(page);
        tabStrip.Controls.Add(button);
        _tabContentPanel.Controls.Add(page);
    }

    private void SelectTab(int index)
    {
        _selectedTabIndex = index;
        for (var item = 0; item < _tabPages.Count; item++)
        {
            _tabPages[item].Visible = item == index;
        }

        StyleTabButtons();
    }

    private void StyleTabButtons()
    {
        var dark = ThemeManager.IsDarkMode();
        for (var index = 0; index < _tabButtons.Count; index++)
        {
            var selected = index == _selectedTabIndex;
            var button = _tabButtons[index];
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = dark
                ? selected ? Color.FromArgb(58, 58, 58) : Color.FromArgb(36, 36, 36)
                : selected ? Color.White : SystemColors.Control;
            button.ForeColor = dark ? Color.FromArgb(242, 242, 242) : SystemColors.ControlText;
            button.FlatAppearance.BorderColor = dark
                ? selected ? Color.FromArgb(82, 82, 82) : Color.FromArgb(56, 56, 56)
                : selected ? Color.FromArgb(150, 150, 150) : Color.FromArgb(210, 210, 210);
            button.FlatAppearance.BorderSize = 1;
        }
    }

    private Control BuildStatusPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Margin = new Padding(0, 0, 0, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.Dock = DockStyle.Top;
        ConfigureStatusLabel(_luxStatusLabel);
        ConfigureStatusLabel(_brightnessStatusLabel);
        ConfigureStatusLabel(_monitorStatusLabel);
        ConfigureStatusLabel(_messageStatusLabel);

        AddStatusPairRow(grid, "Current lux", _luxStatusLabel, "Monitors", _monitorStatusLabel);
        AddStatusPairRow(grid, "Target brightness", _brightnessStatusLabel, "Message", _messageStatusLabel);

        panel.Controls.Add(grid);
        panel.Controls.Add(BuildCurvesPanel());
        return panel;
    }

    private Control BuildAboutPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 5
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "Application",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        });

        var infoGrid = CreateTwoColumnGrid(140);
        infoGrid.Dock = DockStyle.Top;
        AddRow(infoGrid, "Version", new Label { Text = AppVersion, AutoSize = true });
        panel.Controls.Add(infoGrid);

        var linkRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 8)
        };

        var websiteLink = new LinkLabel
        {
            Text = "planetenexpress.de",
            AutoSize = true,
            Margin = new Padding(0, 4, 18, 4)
        };
        websiteLink.LinkClicked += (_, _) => OpenUrl("https://planetenexpress.de");

        var githubLink = new LinkLabel
        {
            Text = "GitHub project",
            AutoSize = true,
            Margin = new Padding(0, 4, 18, 4)
        };
        githubLink.LinkClicked += (_, _) => OpenUrl("https://github.com/afaminx/DDC-CI-HA-Bridge");

        var updateButton = new Button
        {
            Text = "Check for updates",
            AutoSize = true,
            Margin = new Padding(0)
        };
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync(showNoUpdate: true);

        linkRow.Controls.Add(websiteLink);
        linkRow.Controls.Add(githubLink);
        linkRow.Controls.Add(updateButton);
        panel.Controls.Add(linkRow);

        panel.Controls.Add(new Label
        {
            Text = "License",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 6)
        });

        var licenseText = File.Exists("LICENSE.txt")
            ? File.ReadAllText("LICENSE.txt")
            : GetBundledLicenseText();
        var licenseLabel = new Label
        {
            Text = licenseText,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = new Padding(0),
            Padding = new Padding(6, 4, 6, 4),
            Font = new Font("Segoe UI", 8F),
            BorderStyle = BorderStyle.None,
            TextAlign = ContentAlignment.TopLeft
        };
        panel.Controls.Add(licenseLabel);

        return panel;
    }

    private Control BuildCurvesPanel()
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

        var referenceLuxBox = CreateNumberBox(1, 1000000, 1000, referenceLux);
        var monitorBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        var sectionLabel = new Label
        {
            Text = "Brightness curve",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 2, 0, 6)
        };

        var customCurveBox = new CheckBox { Text = "Use monitor-specific curve", AutoSize = true };
        var editor = new CurveEditorControl { Dock = DockStyle.Fill, MinimumSize = new Size(560, 350), ReferenceLux = referenceLux, CurrentLux = _currentLux };
        var selectedLuxBox = CreateNumberBox(0, referenceLux, 100, 0);
        var selectedBrightnessBox = CreateNumberBox(1, 100, 1, 1);
        var addPointButton = new Button { Text = "Add", AutoSize = true };
        var removePointButton = new Button { Text = "Remove", AutoSize = true };
        var applyButton = new Button { Text = "Apply", AutoSize = true };
        selectedLuxBox.Width = 90;
        selectedBrightnessBox.Width = 90;

        monitorBox.Items.Add(new MonitorSelectionItem("Default graph", "", isDefault: true));
        foreach (var monitor in availableMonitors)
        {
            monitorBox.Items.Add(new MonitorSelectionItem(monitor.DisplayName, monitor.Key, isDefault: false));
        }
        monitorBox.SelectedIndex = 0;

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 5
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddFullWidthRow(Control control, SizeType sizeType = SizeType.AutoSize, float height = 0)
        {
            var row = body.RowCount++;
            body.RowStyles.Add(sizeType == SizeType.AutoSize
                ? new RowStyle(SizeType.AutoSize)
                : new RowStyle(sizeType, height));
            body.Controls.Add(control, 0, row);
            body.SetColumnSpan(control, 5);
        }

        void AddCurveSelectorRow()
        {
            var row = body.RowCount++;
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var maxLabel = new Label { Text = "Maximum lux", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
            var targetLabel = new Label { Text = "Curve target", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
            referenceLuxBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            monitorBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            referenceLuxBox.Margin = new Padding(0, 4, 0, 4);
            monitorBox.Margin = new Padding(0, 4, 0, 4);

            body.Controls.Add(maxLabel, 0, row);
            body.Controls.Add(referenceLuxBox, 1, row);
            body.Controls.Add(targetLabel, 3, row);
            body.Controls.Add(monitorBox, 4, row);
        }

        var pointRow = new FlowLayoutPanel
        {
            Dock = DockStyle.None,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        pointRow.Controls.Add(new Label { Text = "Selected lux", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 6) });
        pointRow.Controls.Add(selectedLuxBox);
        pointRow.Controls.Add(new Label { Text = "Brightness (%)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(14, 6, 6, 6) });
        pointRow.Controls.Add(selectedBrightnessBox);
        pointRow.Controls.Add(addPointButton);
        pointRow.Controls.Add(removePointButton);
        pointRow.Controls.Add(applyButton);

        var graphHost = new Panel
        {
            Dock = DockStyle.Fill,
            Height = editor.Height,
            Margin = new Padding(0, 10, 0, 0)
        };
        graphHost.Controls.Add(editor);

        var pointHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0)
        };
        pointHost.Controls.Add(pointRow);
        pointHost.Layout += (_, _) =>
        {
            var preferred = pointRow.GetPreferredSize(Size.Empty);
            pointRow.Size = preferred;
            pointRow.Left = Math.Max(0, pointHost.ClientSize.Width - preferred.Width);
            pointRow.Top = 0;
        };

        AddFullWidthRow(sectionLabel);
        AddCurveSelectorRow();
        AddFullWidthRow(customCurveBox);
        AddFullWidthRow(graphHost, SizeType.Absolute, editor.MinimumSize.Height + 12);
        AddFullWidthRow(pointHost, SizeType.Absolute, pointRow.GetPreferredSize(Size.Empty).Height + 10);

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
                customCurveBox.Visible = false;
                editor.Enabled = true;
                editor.SetPoints(defaultCurve.Points);
            }
            else
            {
                selectedKey = item.MonitorKey;
                customCurveBox.Visible = true;
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
        applyButton.Click += (_, _) =>
        {
            SaveEditorToSelected();
            _settings.ReferenceLux = referenceLuxBox.Value;
            _settings.DefaultCurve = defaultCurve;
            _settings.MonitorCurves = monitorCurves;
            SaveSettings();
            RestartTimer();
            _messageStatusLabel.Text = "Brightness curve applied.";
        };

        LoadSelectedGraph();
        UpdateSelectedPointFields();
        _openCurveEditors.Add(editor);
        return body;
    }

    private Control BuildSettingsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1
        };

        var generalLabel = new Label
        {
            Text = "General",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        };

        _enabledSettingsBox.Text = "Automatic brightness control";
        _enabledSettingsBox.AutoSize = true;
        _enabledSettingsBox.CheckedChanged += (_, _) => ApplyGeneralSettingsFromUi();

        _startupSettingsBox.Text = "Start at Windows sign-in";
        _startupSettingsBox.AutoSize = true;
        _startupSettingsBox.CheckedChanged += (_, _) => ApplyGeneralSettingsFromUi();

        _startMinimizedSettingsBox.Text = "Start minimized in notification area";
        _startMinimizedSettingsBox.AutoSize = true;
        _startMinimizedSettingsBox.CheckedChanged += (_, _) => ApplyGeneralSettingsFromUi();

        _checkUpdatesBox.Text = "Check for updates at startup";
        _checkUpdatesBox.AutoSize = true;
        _checkUpdatesBox.CheckedChanged += (_, _) => ApplyGeneralSettingsFromUi();

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10)
        };
        options.Controls.Add(_enabledSettingsBox);
        options.Controls.Add(_startupSettingsBox);
        options.Controls.Add(_startMinimizedSettingsBox);
        options.Controls.Add(_checkUpdatesBox);

        var connectionLabel = new Label
        {
            Text = "Connection",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 10, 0, 4)
        };

        var addressBox = new TextBox { Text = _settings.HomeAssistantAddress, Dock = DockStyle.Fill };
        var sensorBox = new TextBox { Text = _settings.SensorEntityId, Dock = DockStyle.Fill };
        var tokenBox = new TextBox { Text = _settings.Token, Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        var pollingSecondsBox = CreateNumberBox(5, 3600, 1, Math.Clamp(_settings.PollingSeconds, 5, 3600));

        var connectionGrid = CreateTwoColumnGrid(205);
        connectionGrid.Dock = DockStyle.Top;
        AddRow(connectionGrid, "Home Assistant server", addressBox);
        AddRow(connectionGrid, "Sensor entity", sensorBox);
        AddRow(connectionGrid, "Access token", tokenBox);
        AddRow(connectionGrid, "Polling interval (seconds)", pollingSecondsBox);

        var saveConnectionButton = new Button { Text = "Apply", AutoSize = true };
        saveConnectionButton.Click += (_, _) =>
        {
            _settings.HomeAssistantAddress = addressBox.Text.Trim();
            _settings.SensorEntityId = sensorBox.Text.Trim();
            _settings.Token = tokenBox.Text.Trim();
            _settings.PollingSeconds = (int)pollingSecondsBox.Value;
            SaveSettings();
            RestartTimer();
            _messageStatusLabel.Text = "Connection settings saved.";
        };

        var connectionButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 0)
        };
        connectionButtons.Controls.Add(saveConnectionButton);

        panel.Controls.Add(generalLabel);
        panel.Controls.Add(options);
        panel.Controls.Add(connectionLabel);
        panel.Controls.Add(connectionGrid);
        panel.Controls.Add(connectionButtons);
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
        var pollingSecondsBox = CreateNumberBox(5, 3600, 1, Math.Clamp(_settings.PollingSeconds, 5, 3600));

        var grid = CreateDialogGrid();
        AddRow(grid, "Home Assistant host (IP:port)", addressBox);
        AddRow(grid, "Sensor entity ID", sensorBox);
        AddRow(grid, "Long-lived access token", tokenBox);
        AddRow(grid, "Polling interval (seconds)", pollingSecondsBox);

        var buttons = BuildDialogButtons(dialog, () =>
        {
            _settings.HomeAssistantAddress = addressBox.Text.Trim();
            _settings.SensorEntityId = sensorBox.Text.Trim();
            _settings.Token = tokenBox.Text.Trim();
            _settings.PollingSeconds = (int)pollingSecondsBox.Value;
            SaveSettings();
            RestartTimer();
            return true;
        });
        dialog.Controls.Add(buttons);
        dialog.Controls.Add(grid);
        FitDialogToContent(dialog, grid, buttons, 520);
        ThemeManager.Apply(dialog);

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

        var pollingSecondsBox = CreateNumberBox(5, 3600, 1, Math.Clamp(_settings.PollingSeconds, 5, 3600));
        var referenceLuxBox = CreateNumberBox(1, 1000000, 1000, referenceLux);
        var monitorBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 430 };
        var customCurveBox = new CheckBox { Text = "Use custom graph for this monitor", AutoSize = true };
        var editor = new CurveEditorControl { Size = new Size(700, 320), Anchor = AnchorStyles.Left | AnchorStyles.Right, ReferenceLux = referenceLux, CurrentLux = _currentLux };
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
            _settings.ReferenceLux = referenceLuxBox.Value;
            _settings.DefaultCurve = defaultCurve;
            _settings.MonitorCurves = monitorCurves;

            SaveSettings();
            RestartTimer();
            return true;
        });
        dialog.Controls.Add(buttons);
        dialog.Controls.Add(body);
        FitDialogToContent(dialog, body, buttons, 760);
        ThemeManager.Apply(dialog);

        _openCurveEditors.Add(editor);
        try
        {
            dialog.ShowDialog(this);
        }
        finally
        {
            _openCurveEditors.Remove(editor);
        }
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

    private static void AddStatusPairRow(TableLayoutPanel grid, string leftLabel, Control leftControl, string rightLabel, Control rightControl)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var left = new Label { Text = leftLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) };
        var right = new Label { Text = rightLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(20, 6, 12, 6) };
        leftControl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        rightControl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        leftControl.Margin = new Padding(0, 4, 0, 4);
        rightControl.Margin = new Padding(0, 4, 0, 4);

        grid.Controls.Add(left, 0, row);
        grid.Controls.Add(leftControl, 1, row);
        grid.Controls.Add(right, 2, row);
        grid.Controls.Add(rightControl, 3, row);
    }

    private static void AddWideRow(TableLayoutPanel grid, string labelText, Control control)
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
        grid.SetColumnSpan(control, 3);
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

    private void SaveSettings()
    {
        _settings.Save();
        SyncSettingsControls();
    }

    private void SyncSettingsControls()
    {
        _updatingSettingsControls = true;
        _enabledSettingsBox.Checked = _settings.Enabled;
        _startupSettingsBox.Checked = IsStartupEnabled();
        _startMinimizedSettingsBox.Checked = _settings.StartMinimized;
        _checkUpdatesBox.Checked = _settings.CheckForUpdates;
        _updatingSettingsControls = false;
    }

    private void ApplyGeneralSettingsFromUi()
    {
        if (_updatingSettingsControls)
        {
            return;
        }

        _settings.Enabled = _enabledSettingsBox.Checked;
        _settings.StartMinimized = _startMinimizedSettingsBox.Checked;
        _settings.CheckForUpdates = _checkUpdatesBox.Checked;

        try
        {
            StartupManager.SetEnabled(_startupSettingsBox.Checked, _startMinimizedSettingsBox.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Startup setting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        SaveSettings();
        RestartTimer();
    }

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

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!_settings.CheckForUpdates)
        {
            return;
        }

        await CheckForUpdatesAsync(showNoUpdate: false);
    }

    private async Task CheckForUpdatesAsync(bool showNoUpdate)
    {
        try
        {
            var release = await _updateChecker.GetNewerReleaseAsync(AppVersion, CancellationToken.None);
            if (release is null || IsDisposed)
            {
                if (showNoUpdate)
                {
                    MessageBox.Show(this, "You are using the latest available release.", "No update available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return;
            }

            var result = MessageBox.Show(
                this,
                $"A newer release is available: {release.TagName}.{Environment.NewLine}{Environment.NewLine}Open the GitHub project page now?",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
            {
                OpenUrl("https://github.com/afaminx/DDC-CI-HA-Bridge");
            }
        }
        catch (Exception ex)
        {
            if (showNoUpdate)
            {
                MessageBox.Show(this, "Could not check for updates: " + ex.Message, "Update check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
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
            SetCurrentLux(lux);

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
        SyncSettingsControls();
    }

    private void SetCurrentLux(decimal lux)
    {
        _currentLux = lux;
        foreach (var editor in _openCurveEditors.ToArray())
        {
            if (!editor.IsDisposed)
            {
                editor.CurrentLux = lux;
            }
        }
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

    private static void OpenUrl(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static string GetBundledLicenseText()
    {
        return """
MIT License

Copyright (c) 2026 planetenexpress.de

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
""";
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
