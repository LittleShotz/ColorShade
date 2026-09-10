using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ColorShade.Native;
using ColorShade.Recovery;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ColorShade;

public partial class MainWindow : Window
{
    private readonly PresetStore _presetStore = new(AppPaths.Data);
    private List<Preset> _presets = [];
    private Settings _settings = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private GuardianClient? _client;
    private Forms.NotifyIcon? _tray;
    private Drawing.Icon? _icon;
    private bool _loading = true, _busy, _exiting, _sessionBlocked, _suspended, _refresh;
    private long _previewUntil;
    private string? _previewMonitor;
    private string _lastLog = "";
    private readonly List<string> _loadWarnings = [];

    public MainWindow()
    {
        InitializeComponent();
        try { _presets = _presetStore.Load(); }
        catch (Exception ex)
        {
            _presets = Preset.Defaults();
            _loadWarnings.Add("Could not load presets. The original file was retained: " + ex.Message);
        }
        try
        {
            if (File.Exists(AppPaths.Settings)) _settings = JsonDisk.Read<Settings>(AppPaths.Settings);
            if (_settings.SchemaVersion != 1) throw new InvalidDataException("Unsupported settings format.");
        }
        catch (Exception ex) { _settings = new() { Enabled = false }; _loadWarnings.Add(ex.Message); }
        PresetCombo.ItemsSource = _presets;
        PresetCombo.SelectedItem = _presets.FirstOrDefault(p => p.Id == _settings.SelectedPresetId) ?? _presets[0];
        LoadSliders();
        EnabledToggle.IsChecked = _settings.Enabled;
        EnabledLabel.Text = _settings.Enabled ? "Enabled" : "Disabled";
        try { StartupCheck.IsChecked = StartupRegistration.IsEnabled(); }
        catch (Exception ex) { _loadWarnings.Add("Startup setting could not be read: " + ex.Message); }
        CreateTray();
        _timer.Tick += async (_, _) => await TickAsync();
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        _loading = false;
    }

    internal async Task StartAsync()
    {
        try
        {
            _client = new GuardianClient();
            UpdateStatus(await _client.StartAsync());
            _timer.Start();
            if (_loadWarnings.Count > 0)
            {
                OpenWindow();
                MessageBox.Show(this, string.Join("\n\n", _loadWarnings), "ColorShade settings", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex) { HelperFailed(ex); }
    }

    private async Task TickAsync()
    {
        if (_busy || _exiting || _client is null) return;
        _busy = true;
        try
        {
            bool preview = _previewUntil > Environment.TickCount64;
            string? monitor = null;
            if (!_sessionBlocked && !_suspended)
                monitor = preview ? _previewMonitor : _settings.Enabled ? ForegroundDetector.RustMonitor() : null;
            if (!preview) { _previewUntil = 0; TestButton.Content = "Test for 10 seconds"; }
            var command = new Command(monitor, (int)VibranceSlider.Value, (int)BrightnessSlider.Value, preview, _refresh);
            _refresh = false;
            Reply reply = await _client.SendAsync(command);
            if (!_exiting) UpdateStatus(reply);
        }
        catch (Exception ex) { if (!_exiting) HelperFailed(ex); }
        finally { _busy = false; }
    }

    private void HelperFailed(Exception ex)
    {
        AppPaths.Log("Helper connection failed: " + ex.Message);
        DisconnectGuardian();
        _settings.Enabled = false;
        _loading = true; EnabledToggle.IsChecked = false; _loading = false;
        EnabledLabel.Text = "Disabled";
        _previewUntil = 0;
        UpdateStatus(new(false, "Recovery helper stopped", "Automatic adjustment is disabled. Recovery was requested. Use Restore / Retry to reconnect.",
            "Disconnected", [], true));
        try { AppPaths.LaunchSelf("--restore", "--quiet"); }
        catch (Exception error) { AppPaths.Log("Recovery launch: " + error.Message); }
        RebuildTrayMenu();
    }

    private void UpdateStatus(Reply reply)
    {
        StatusText.Text = !_settings.Enabled && !reply.Active && !reply.RecoveryPending && reply.Status == "Idle (Desktop)"
            ? "Disabled (Desktop)" : reply.Status;
        DetailText.Text = reply.Detail;
        BackendText.Text = reply.Backend;
        StatusDot.Fill = new SolidColorBrush(reply.Active ? Color.FromRgb(111, 222, 165)
            : reply.RecoveryPending || reply.Status.Contains("paused") || reply.Status.Contains("error")
                ? Color.FromRgb(255, 190, 105) : Color.FromRgb(162, 175, 195));
        MonitorText.Text = string.Join("\n", reply.Displays.Select(d => d.Label + (d.CanAdjust ? " · SDR" : " · " + d.BlockReason)));
        if (_tray is not null) _tray.Text = ("ColorShade · " + StatusText.Text)[..Math.Min(63, ("ColorShade · " + StatusText.Text).Length)];
        string message = reply.Status + " | " + reply.Detail;
        if (_lastLog != message) { AppPaths.Log(message); _lastLog = message; }
    }

    private void LoadSliders()
    {
        if (PresetCombo.SelectedItem is not Preset preset) return;
        VibranceSlider.Value = preset.Vibrance; BrightnessSlider.Value = preset.Brightness;
        DeleteButton.IsEnabled = !preset.BuiltIn;
        PresetHint.Text = preset.BuiltIn ? "Built-in · Save makes a copy" : "Custom preset";
        UpdateValues();
    }
    private void UpdateValues()
    {
        if (VibranceValue is null || BrightnessValue is null || VibranceSlider is null || BrightnessSlider is null) return;
        VibranceValue.Text = Math.Round(VibranceSlider.Value) + "%";
        BrightnessValue.Text = Math.Round(BrightnessSlider.Value) + "%";
    }
    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateValues();
        if (!_loading && PresetCombo.SelectedItem is Preset p
            && ((int)VibranceSlider.Value != p.Vibrance || (int)BrightnessSlider.Value != p.Brightness))
            PresetHint.Text = "Unsaved changes";
    }
    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _loading = true; LoadSliders(); _loading = false;
        if (PresetCombo.SelectedItem is Preset preset) _settings.SelectedPresetId = preset.Id;
        SaveSettings(); RebuildTrayMenu();
    }
    private void Enabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.Enabled = EnabledToggle.IsChecked == true;
        EnabledLabel.Text = _settings.Enabled ? "Enabled" : "Disabled";
        if (!_settings.Enabled) _previewUntil = 0;
        SaveSettings(); RebuildTrayMenu();
        _ = TickAsync();
    }
    private void SaveSettings()
    {
        try { JsonDisk.Write(AppPaths.Settings, _settings); }
        catch (Exception ex) { ShowError("Settings could not be saved", ex); }
    }

    private void New_Click(object sender, RoutedEventArgs e) => SavePreset(createNew: true);
    private void Save_Click(object sender, RoutedEventArgs e) => SavePreset(createNew: false);
    private void SavePreset(bool createNew)
    {
        var current = (Preset)PresetCombo.SelectedItem;
        createNew |= current.BuiltIn;
        string initial = createNew ? (current.BuiltIn ? "My " + current.Name : current.Name + " copy") : current.Name;
        var dialog = new PresetNameDialog(initial) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        string name = dialog.PresetName;
        if (_presets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && (createNew || p.Id != current.Id)))
        { MessageBox.Show(this, "A preset already has that name.", "ColorShade"); return; }
        var updated = new Preset(createNew ? Guid.NewGuid().ToString("N") : current.Id,
            name, (int)VibranceSlider.Value, (int)BrightnessSlider.Value);
        var next = _presets.ToList();
        if (createNew) next.Add(updated); else next[next.FindIndex(p => p.Id == current.Id)] = updated;
        try
        {
            _presetStore.Save(next); _presets = next;
            _loading = true; PresetCombo.ItemsSource = _presets; PresetCombo.SelectedItem = updated; LoadSliders(); _loading = false;
            _settings.SelectedPresetId = updated.Id; SaveSettings(); RebuildTrayMenu();
        }
        catch (Exception ex) { _loading = false; ShowError("Preset could not be saved", ex); }
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not Preset current || current.BuiltIn) return;
        var next = _presets.Where(p => p.Id != current.Id).ToList();
        try
        {
            _presetStore.Save(next); _presets = next;
            _loading = true; PresetCombo.ItemsSource = _presets; PresetCombo.SelectedIndex = 0; LoadSliders(); _loading = false;
            _settings.SelectedPresetId = _presets[0].Id; SaveSettings(); RebuildTrayMenu();
        }
        catch (Exception ex) { _loading = false; ShowError("Preset could not be deleted", ex); }
    }
    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try { StartupRegistration.SetEnabled(StartupCheck.IsChecked == true); }
        catch (Exception ex)
        {
            _loading = true; StartupCheck.IsChecked = false; _loading = false;
            ShowError("Start with Windows could not be changed", ex);
        }
    }
    private void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) { MessageBox.Show(this, "Use Restore / Retry to start the recovery helper first.", "ColorShade"); return; }
        if (_previewUntil > Environment.TickCount64) { _previewUntil = 0; TestButton.Content = "Test for 10 seconds"; }
        else
        {
            _previewMonitor = ForegroundDetector.MonitorForWindow(new WindowInteropHelper(this).Handle);
            _previewUntil = Environment.TickCount64 + 10_000;
            TestButton.Content = "Stop preview";
        }
        _ = TickAsync();
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        _previewUntil = 0;
        // Disable automatic reapplication so the user can inspect desktop recovery.
        EnabledToggle.IsChecked = false;
        _refresh = true;
        if (_client is null && !_busy) await StartAsync();
        await TickAsync();
    }

    private async void Exit_Click(object sender, RoutedEventArgs e) => await ExitAsync();
    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true; _timer.Stop();
        try
        {
            if (_client is not null)
            {
                var reply = await _client.SendAsync(new Command(Stop: true));
                if (reply.RecoveryPending) AppPaths.Log("Exit: restoration will be retried by guardian and on next launch.");
            }
        }
        catch (Exception ex) { AppPaths.Log("Exit: " + ex.Message); }
        finally { DisconnectGuardian(); DisposeTray(); Application.Current.Shutdown(); }
    }
    internal void DisconnectGuardian()
    {
        // May also run from an exception handler outside the UI thread.
        var client = Interlocked.Exchange(ref _client, null);
        try { client?.Dispose(); } catch { }
    }
    internal void DisposeTray()
    {
        SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); _tray = null; }
        _icon?.Dispose(); _icon = null;
    }
    internal void OpenWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    { if (!_exiting) { e.Cancel = true; Hide(); _previewUntil = 0; } }
    private void Window_StateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized) { Hide(); _previewUntil = 0; } }
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        int dark = 1;
        Win32.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
    }
    private void ShowError(string title, Exception ex)
    { AppPaths.Log(title + ": " + ex); MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error); }
    private void SessionChanged(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        { _sessionBlocked = true; _previewUntil = 0; }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            _sessionBlocked = false;
        _refresh = true; _ = TickAsync();
    }));
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (e.Mode == PowerModes.Suspend) { _suspended = true; _previewUntil = 0; }
        if (e.Mode == PowerModes.Resume) _suspended = false;
        _refresh = true; _ = TickAsync();
    }));
    // Inventory is re-read every tick. Do not force a reapply on display events:
    // a driver may emit one for our own color write, creating a feedback loop.
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() => { _previewUntil = 0; _ = TickAsync(); }));

    private void CreateTray()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(26, 33, 47));
            graphics.FillEllipse(background, 0, 0, 31, 31);
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(141, 181, 255), 5);
            graphics.DrawArc(pen, 7, 7, 18, 18, 45, 270);
            using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(111, 222, 165));
            graphics.FillEllipse(dot, 23, 22, 7, 7);
        }
        IntPtr handle = bitmap.GetHicon();
        try { using var borrowed = Drawing.Icon.FromHandle(handle); _icon = (Drawing.Icon)borrowed.Clone(); }
        finally { Win32.DestroyIcon(handle); }
        Icon = Imaging.CreateBitmapSourceFromHIcon(_icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "ColorShade", Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(OpenWindow));
        RebuildTrayMenu();
    }
    private void RebuildTrayMenu()
    {
        if (_tray is null) return;
        var menu = new Forms.ContextMenuStrip { BackColor = Drawing.Color.FromArgb(25, 31, 43), ForeColor = Drawing.Color.White };
        menu.Renderer = new Forms.ToolStripProfessionalRenderer(new TrayColors());
        menu.Items.Add("Open", null, (_, _) => Dispatcher.BeginInvoke(new Action(OpenWindow)));
        menu.Items.Add(_settings.Enabled ? "Disable ColorShade" : "Enable ColorShade", null,
            (_, _) => Dispatcher.BeginInvoke(new Action(() => EnabledToggle.IsChecked = !_settings.Enabled)));
        var presets = new Forms.ToolStripMenuItem("Presets");
        foreach (var preset in _presets)
        {
            var item = new Forms.ToolStripMenuItem(preset.Name) { Checked = preset.Id == _settings.SelectedPresetId, ForeColor = Drawing.Color.White };
            item.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => PresetCombo.SelectedItem = preset));
            presets.DropDownItems.Add(item);
        }
        presets.DropDown.BackColor = Drawing.Color.FromArgb(25, 31, 43);
        presets.DropDown.ForeColor = Drawing.Color.White;
        presets.DropDown.Renderer = menu.Renderer;
        menu.Items.Add(presets); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Restore / Retry", null, (_, _) => Dispatcher.BeginInvoke(new Action(() => Restore_Click(this, new RoutedEventArgs()))));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(new Action(async () => await ExitAsync())));
        var old = _tray.ContextMenuStrip; _tray.ContextMenuStrip = menu; old?.Dispose();
    }

    private sealed class TrayColors : Forms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(25, 31, 43);
        public override Drawing.Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Drawing.Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Drawing.Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(50, 68, 99);
        public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(90, 120, 175);
        public override Drawing.Color MenuBorder => Drawing.Color.FromArgb(61, 74, 96);
    }
}
