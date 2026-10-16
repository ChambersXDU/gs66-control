using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Gs66Control.Core;
using Gs66Control.Windows;
using Forms = System.Windows.Forms;

namespace Gs66Control.Desktop;

public partial class MainWindow : Window
{
    private readonly string? smokeOutput;
    private readonly SettingsStore store;
    private readonly string settingsDirectory;
    private readonly DispatcherTimer timer = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<double> history = [];
    private readonly List<(TextBox Temperature, TextBox Speed)> cpuPoints = [], gpuPoints = [];
    private ConsoleSettings settings = ConsoleSettings.Default;
    private HardwareSnapshot? identity;
    private TelemetrySnapshot? latest;
    private AutoFanPreview? fanPreview;
    private SavedFanCurves? deviceCurves;
    private ITelemetryProbe? probe;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private Forms.ToolStripMenuItem? trayProfiles;
    private string? activeProfileName;
    private ConsoleProfile? activeProfile;
    private HwndSource? windowSource;
    private bool hotkeyRegistered, busy, exiting;
    private bool fanActionBusy;
    private readonly SemaphoreSlim fanPreviewGate = new(1, 1);
    private readonly DispatcherTimer curveHeartbeat = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? activeCurveId;
    private bool profileActionBusy;
    private bool powerPlanActionBusy;
    private WindowsPerformanceSnapshot? performanceSnapshot;
    private string? editingName;
    private int? balancePosition;
    private bool fillingCurve, settingBalance;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public MainWindow(string? smokeOutput = null)
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
        this.smokeOutput = smokeOutput;
        settingsDirectory = smokeOutput ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GS66Control");
        Directory.CreateDirectory(settingsDirectory);
        store = new SettingsStore(Path.Combine(settingsDirectory, "settings.json"));
        try { settings = smokeOutput is null ? store.Load() : ConsoleSettings.Default; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { SettingsStatusText.Text = "设置读取失败"; ActionDiagnosticText.Text = ex.Message; }
        InitializeTuningStore(settingsDirectory);
        CreateEditor(CpuCurveEditor, cpuPoints);
        CreateEditor(GpuCurveEditor, gpuPoints);
        FillPreferences();
        InitializePersonalTuning();
        NewProfile();
        ReloadProfiles();
        ProfilePerformanceChoice.SelectedIndex = 0;
        ProfilePerformanceChoice.SelectionChanged += (_, _) => { if (!settingBalance) SetBalanceIndicator(null); UpdateDraftBadge(); };
        ProfileName.TextChanged += (_, _) => UpdateDraftBadge();
        curveHeartbeat.Tick += (_, _) => UpdateCurveHeartbeat();
        fanReadHeartbeat.Tick += (_, _) => TickFanReader();
        timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => await StartAsync();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5) { _ = Pages.SelectedIndex == 3 ? PreviewFanAsync() : RefreshAsync(); e.Handled = true; }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { SaveProfile(); e.Handled = true; }
        };
        SourceInitialized += (_, _) => { if (smokeOutput is null) RegisterWindowHotkey(); };
        Closing += OnClosing;
        Navigation.SelectedIndex = 0;
        Closed += (_, _) =>
        {
            RequestCurveStop(); curveHeartbeat.Stop();
            StopFanReader();
            activeCurveWorker?.Dispose();
            FinishTuningRecording();
            timer.Stop(); lifetime.Cancel(); tray?.Dispose(); trayIcon?.Dispose();
            if (hotkeyRegistered && windowSource is not null) UnregisterHotKey(windowSource.Handle, 1);
            windowSource?.RemoveHook(HotkeyHook);
        };
    }

    private void NavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is ListBoxItem item && Pages is not null && int.TryParse(item.Tag?.ToString(), out var page))
            Pages.SelectedIndex = page;
    }

    private void PageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Pages is null || !ReferenceEquals(e.Source, Pages) || Pages.SelectedIndex < 0) return;
        PageTitle.Text = new[] { "概览", "方案", "设置", "风扇", "个人调优" }[Pages.SelectedIndex];
        UpdateDraftBadge();
        RefreshButton.Visibility = Pages.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordTuningButton.Visibility = Pages.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshFanReadButton();
        foreach (ListBoxItem item in Navigation.Items)
            if (item.Tag?.ToString() == Pages.SelectedIndex.ToString()) Navigation.SelectedItem = item;
        if (Pages.SelectedIndex == 3 && smokeOutput is null && identity is not null && !fanActionBusy)
        { RefreshPendingRecovery(); _ = PreviewFanAsync(); }
        if (Pages.SelectedIndex == 4 && identity is not null) _ = RefreshPersonalTuningAsync();
    }

    private async Task StartAsync()
    {
        if (smokeOutput is null) CreateTray();
        try
        {
            identity = await new WindowsWmiProbe().CaptureAsync(lifetime.Token);
            probe = new WindowsTelemetryProbe(identity);
            IdentityText.Text = $"{identity.Model}\n{identity.Cpu}\n{string.Join(" / ", identity.Gpus)}\nBIOS {identity.BiosVersion ?? "未知"} · EC {identity.MsiEcFirmware?.Version ?? "未知"}（MSI WMI 报告）\n可见 MSI 类：{identity.MsiWmiClasses.Count}";
            await RefreshAsync();
            await RefreshPerformanceAsync();
            await RefreshPowerPlansAsync();
            await RefreshPersonalTuningAsync();
            RefreshPendingRecovery();
            if (smokeOutput is null)
            {
                try { deviceCurves = await CaptureSavedCurvesAsync(); SetBalanceIndicator(balancePosition); }
                catch (Exception ex) { ActionDiagnosticText.Text = ex.Message; }
            }
            if (smokeOutput is not null) await VerifyUiAsync();
            else timer.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ConnectionText.Text = "连接失败";
            TelemetryWarningText.Text = ex.Message;
            if (smokeOutput is not null) FinishSmokeFailure(ex);
        }
    }

    private async Task RefreshAsync()
    {
        if (busy || probe is null || lifetime.IsCancellationRequested) return;
        busy = true;
        RefreshButton.IsEnabled = false;
        try
        {
            var snapshot = ReadFanFrame(await probe.CaptureAsync(lifetime.Token));
            latest = snapshot;
            RefreshFanReadButton();
            CollectTuningSnapshot(snapshot);
            CpuText.Text = Percent(snapshot.CpuLoadPercent);
            MemoryText.Text = Percent(snapshot.MemoryUsedPercent);
            BatteryText.Text = snapshot.BatteryPercent is { } battery ? $"{battery}%" : "—";
            Fan1Text.Text = Rpm(snapshot.CpuFanRpm);
            Fan2Text.Text = Rpm(snapshot.GpuFanRpm);
            CpuTemperatureText.Text = Celsius(snapshot.CpuTemperatureCelsius);
            GpuTemperatureText.Text = snapshot.GpuTemperatureCelsius is null && snapshot.GpuPower?.IsFreshD3(false, DateTimeOffset.UtcNow) == true
                ? "休眠" : Celsius(snapshot.GpuTemperatureCelsius);
            ConnectionText.Text = snapshot.Warnings.Count > 0 ? "部分数据不可用" : "已连接";
            TelemetryWarningText.Text = string.Join("\n", snapshot.Warnings);
            StatusText.Text = $"{snapshot.CapturedAtUtc.ToLocalTime():HH:mm:ss} 更新";
            if (snapshot.CpuLoadPercent is { } value)
            {
                history.Add(value);
                if (history.Count > 60) history.RemoveAt(0);
                HistoryLine.Points = new PointCollection(history.Select((v, i) => new Point(i * 900.0 / 59, 99 - v * 0.98)));
                HistoryEmptyText.Visibility = Visibility.Collapsed;
            }
            if (tray is not null) tray.Text = $"GS66 · CPU {CpuText.Text} · 风扇 {snapshot.CpuFanRpm?.ToString() ?? "N/A"} RPM";
            if (Pages.SelectedIndex == 4 && DateTimeOffset.UtcNow - lastPersonalRefresh > TimeSpan.FromSeconds(10))
                await RefreshPersonalTuningAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            CpuText.Text = MemoryText.Text = BatteryText.Text = Fan1Text.Text = Fan2Text.Text = CpuTemperatureText.Text = GpuTemperatureText.Text = "—";
            ConnectionText.Text = "更新失败";
            TelemetryWarningText.Text = ex.Message;
            StatusText.Text = latest is null ? "" : $"上次更新 {latest.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
        }
        finally { busy = false; RefreshButton.IsEnabled = true; }
    }

    private static string Percent(double? value) => value is { } number ? $"{number:F0}%" : "—";
    private static string Celsius(int? value) => value is { } number ? $"{number}°" : "—";
    private static string Rpm(int? value) => value is { } number ? $"{number:N0} rpm" : "—";

    private void CreateTray()
    {
        using var iconResource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/gs66-control.ico"))?.Stream;
        if (iconResource is not null)
        { using var loadedIcon = new System.Drawing.Icon(iconResource); trayIcon = (System.Drawing.Icon)loadedIcon.Clone(); }
        tray = new Forms.NotifyIcon { Icon = trayIcon ?? System.Drawing.SystemIcons.Application, Text = "GS66 Control", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示控制台", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("刷新读数", null, (_, _) => Dispatcher.Invoke(() => _ = RefreshAsync()));
        trayProfiles = new Forms.ToolStripMenuItem("方案"); menu.Items.Add(trayProfiles); RefreshTrayProfiles();
        personalTray = new Forms.ToolStripMenuItem("个人调优"); menu.Items.Add(personalTray); RefreshPersonalTray();
        menu.Items.Add("原厂自动风扇模式…", null, (_, _) => Dispatcher.Invoke(() =>
        {
            ShowWindow();
            Pages.SelectedIndex = 3;
            ApplyAutomaticClick(this, new RoutedEventArgs());
        }));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Exit));
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowWindow); };
    }

    private void RegisterWindowHotkey()
    {
        windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (windowSource is null) return;
        windowSource.AddHook(HotkeyHook);
        hotkeyRegistered = RegisterHotKey(windowSource.Handle, 1, 0x4003, 0x47);
        if (!hotkeyRegistered) SettingsStatusText.Text = "Ctrl+Alt+G 已被占用";
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == 1) { ShowWindow(); handled = true; }
        return IntPtr.Zero;
    }

    private void ShowWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Exit() { exiting = true; Close(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (cpuPolicyBusy)
        {
            e.Cancel = true; exiting = false;
            PersonalActionStatus.Text = "正在完成 CPU 参数回读，请稍后退出。";
            return;
        }
        if (!exiting && smokeOutput is null && settings.CloseToTray && tray is not null) { e.Cancel = true; Hide(); }
    }

    private void CreateEditor(StackPanel container, List<(TextBox, TextBox)> points)
    {
        for (var i = 0; i < 6; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var label = new TextBlock { Text = $"{i + 1}", Width = 16, Foreground = Brushes.Gray, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var temperature = new TextBox { Width = 40, IsReadOnly = true, IsTabStop = false, ToolTip = "设备温度点", MaxLength = 3, Padding = new Thickness(4), Margin = new Thickness(0, 2, 6, 2), BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Center };
            var speed = new TextBox { Width = 44, MaxLength = 3, Padding = new Thickness(4), Margin = new Thickness(0, 2, 0, 2) };
            var slider = new Slider { Minimum = 0, Maximum = 100, Width = 70, IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1, LargeChange = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var channel = ReferenceEquals(points, cpuPoints) ? "CPU" : "GPU";
            System.Windows.Automation.AutomationProperties.SetName(temperature, $"{channel} 温度 {i + 1}");
            System.Windows.Automation.AutomationProperties.SetName(speed, $"{channel} 速度值 {i + 1}");
            System.Windows.Automation.AutomationProperties.SetName(slider, $"{channel} 风扇速度 {i + 1}");
            slider.ValueChanged += (_, _) => speed.Text = ((int)slider.Value).ToString();
            speed.TextChanged += (_, _) => { if (int.TryParse(speed.Text, out var value) && value is >= 0 and <= 100 && slider.Value != value) slider.Value = value; };
            row.Children.Add(label); row.Children.Add(temperature); row.Children.Add(slider); row.Children.Add(speed);
            container.Children.Add(row); points.Add((temperature, speed));
            temperature.TextChanged += (_, _) => UpdateCurveGraphs();
            speed.TextChanged += (_, _) => { UpdateCurveGraphs(); if (!fillingCurve && !settingBalance) SetBalanceIndicator(null); UpdateDraftBadge(); };
        }
    }

    private void UpdateCurveGraphs()
    {
        CpuCurveLine.Points = CurveGraph(cpuPoints);
        GpuCurveLine.Points = CurveGraph(gpuPoints);
    }

    private static PointCollection CurveGraph(List<(TextBox Temperature, TextBox Speed)> boxes)
    {
        try
        {
            var points = ReadCurve(boxes);
            if (points.Count != 6 || (ConsoleSettings.Default with { Profiles = [new("preview", points, points)] }).Validate().Count > 0) return [];
            return new PointCollection(points.Select(p => new Point(p.Temperature * 4.0, 100 - p.Speed)));
        }
        catch (InvalidDataException) { return []; }
    }

    private async Task<bool> ReadDeviceCurvesAsync()
    {
        ReadDeviceCurveButton.IsEnabled = false;
        var editorBefore = EditorFingerprint();
        try
        {
            deviceCurves = await CaptureSavedCurvesAsync();
            if (editorBefore != EditorFingerprint())
            { ProfileStatusText.Text = "草稿已更改，请重新读取。"; return false; }
            NewProfile();
            ProfileName.Text = "设备曲线 " + deviceCurves.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss");
            FillCurve(cpuPoints, deviceCurves.Cpu); FillCurve(gpuPoints, deviceCurves.Gpu);
            ProfileStatusText.Text = "已读取设备曲线";
            return true;
        }
        catch (Exception ex) { ActionDiagnosticText.Text = ex.Message; ProfileStatusText.Text = "读取失败，详见诊断。"; return false; }
        finally { ReadDeviceCurveButton.IsEnabled = true; }
    }

    private string EditorFingerprint() => string.Join("\0", cpuPoints.Concat(gpuPoints)
        .SelectMany(p => new[] { p.Temperature.Text, p.Speed.Text }).Prepend(ProfileName.Text).Prepend(editingName ?? "")
        .Prepend(SelectedShift(ProfilePerformanceChoice)?.ToString() ?? "").Prepend(balancePosition?.ToString() ?? ""));

    private async void ReadDeviceCurveClick(object sender, RoutedEventArgs e) => await ReadDeviceCurvesAsync();

    private void NewProfile()
    {
        editingName = null;
        ProfileList.SelectedIndex = -1;
        ProfileName.Text = "我的方案";
        var curve = new[] { new CurvePoint(50, 35), new CurvePoint(60, 50), new CurvePoint(70, 60), new CurvePoint(80, 75), new CurvePoint(90, 90), new CurvePoint(95, 100) };
        FillCurve(cpuPoints, deviceCurves?.Cpu ?? curve); FillCurve(gpuPoints, deviceCurves?.Gpu ?? curve);
        ProfilePerformanceChoice.SelectedIndex = 0;
        SetBalanceIndicator(null);
        ProfileStatusText.Text = "";
    }

    private void FillCurve(List<(TextBox Temperature, TextBox Speed)> boxes, IReadOnlyList<CurvePoint> curve)
    {
        fillingCurve = true;
        try { for (var i = 0; i < boxes.Count; i++) { boxes[i].Temperature.Text = curve[i].Temperature.ToString(); boxes[i].Speed.Text = curve[i].Speed.ToString(); } }
        finally { fillingCurve = false; }
    }

    private static IReadOnlyList<CurvePoint> ReadCurve(List<(TextBox Temperature, TextBox Speed)> boxes) => boxes.Select(pair =>
        int.TryParse(pair.Temperature.Text, out var temperature) && int.TryParse(pair.Speed.Text, out var speed)
            ? new CurvePoint(temperature, speed) : throw new InvalidDataException("曲线只能填写整数。")).ToArray();

    private ConsoleProfile ReadProfile() => new(ProfileName.Text.Trim(), ReadCurve(cpuPoints), ReadCurve(gpuPoints)) { PerformanceShift = SelectedShift(ProfilePerformanceChoice), BalancePosition = balancePosition };

    private void UpdateDraftBadge()
    {
        if (DraftBadge is null || Pages is null) return;
        var matches = false;
        if (activeProfile is { } applied)
        {
            try
            {
                var draft = ReadProfile();
                matches = draft.Name == applied.Name && draft.PerformanceShift == applied.PerformanceShift &&
                    draft.Cpu.SequenceEqual(applied.Cpu) && draft.Gpu.SequenceEqual(applied.Gpu);
            }
            catch (InvalidDataException) { }
        }
        DraftBadge.Visibility = Pages.SelectedIndex == 1 && !matches ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool SaveProfile()
    {
        try
        {
            var profile = ReadProfile();
            var profiles = settings.Profiles.Where(p => p.Name != editingName).Append(profile).ToArray();
            var next = settings with { Profiles = profiles };
            store.Save(next); settings = next; editingName = profile.Name;
            ReloadProfiles(profile.Name);
            ProfileStatusText.Text = "已保存";
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { ProfileStatusText.Text = ex.Message; return false; }
    }

    private void ReloadProfiles(string? selected = null)
    {
        ProfileList.ItemsSource = settings.Profiles.Select(p => p.Name).ToArray();
        if (selected is not null) ProfileList.SelectedItem = selected;
        RefreshTrayProfiles();
    }

    private void ProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileList.SelectedItem is not string name) return;
        var profile = settings.Profiles.First(p => p.Name == name);
        editingName = name; ProfileName.Text = name; FillCurve(cpuPoints, profile.Cpu); FillCurve(gpuPoints, profile.Gpu);
        SelectShift(ProfilePerformanceChoice, profile.PerformanceShift);
        SetBalanceIndicator(profile.BalancePosition);
        ProfileStatusText.Text = "";
    }

    private void FillPreferences()
    {
        RefreshInterval.Text = settings.RefreshSeconds.ToString(); CloseToTray.IsChecked = settings.CloseToTray;
        AccentChoice.SelectedIndex = settings.Accent == "#78A9FF" ? 1 : settings.Accent == "#C29CFF" ? 2 : 0;
        ApplyPreferences();
    }

    private void ApplyPreferences()
    {
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
        var accent = settings.Accent == "#78A9FF" ? "#0078D4" : settings.Accent == "#C29CFF" ? "#7752A6" : "#008573";
        Resources["Accent"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent));
    }

    private bool SavePreferences()
    {
        try
        {
            if (!int.TryParse(RefreshInterval.Text, out var interval)) throw new InvalidDataException("刷新间隔必须是整数。");
            var accent = ((ComboBoxItem)AccentChoice.SelectedItem).Tag.ToString()!;
            var next = settings with { RefreshSeconds = interval, CloseToTray = CloseToTray.IsChecked == true, Accent = accent };
            store.Save(next); settings = next; ApplyPreferences();
            SettingsStatusText.Text = "设置已保存"; return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { SettingsStatusText.Text = ex.Message; return false; }
    }

    private void ExportProfileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = ReadProfile();
            var validation = settings with { Profiles = [profile] };
            var errors = validation.Validate();
            if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON 方案|*.json", FileName = "gs66-profile.json" };
            if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(profile, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { ProfileStatusText.Text = ex.Message; }
    }

    private void ImportProfileClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON 方案|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            if (new FileInfo(dialog.FileName).Length > 64 * 1024) throw new InvalidDataException("方案文件超过 64 KB。");
            var profile = JsonSerializer.Deserialize<ConsoleProfile>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("方案文件为空。");
            var errors = (settings with { Profiles = [profile] }).Validate();
            if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
            editingName = null; ProfileList.SelectedIndex = -1; ProfileName.Text = profile.Name;
            FillCurve(cpuPoints, profile.Cpu); FillCurve(gpuPoints, profile.Gpu);
            SelectShift(ProfilePerformanceChoice, profile.PerformanceShift);
            SetBalanceIndicator(profile.BalancePosition);
            ProfileStatusText.Text = "已导入";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { ProfileStatusText.Text = ex.Message; }
    }

    private void DeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (editingName is null) return;
        if (MessageBox.Show(this, $"删除本地方案“{editingName}”？", "删除方案", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { var next = settings with { Profiles = settings.Profiles.Where(p => p.Name != editingName).ToArray() }; store.Save(next); settings = next; ReloadProfiles(); NewProfile(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { ProfileStatusText.Text = ex.Message; }
    }

    private void ExportDiagnosticClick(object sender, RoutedEventArgs e)
    {
        if (identity is null) { SettingsStatusText.Text = "诊断尚未完成。"; return; }
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON 诊断|*.json", FileName = "gs66-diagnostic.json" };
            if (dialog.ShowDialog(this) == true)
            {
                File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new { identity, telemetry = latest, autoFanPreview = fanPreview, deviceCurves, actionError = ActionDiagnosticText.Text }, JsonOptions));
                SettingsStatusText.Text = "已导出";
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { SettingsStatusText.Text = ex.Message; }
    }

    private async Task PreviewFanAsync()
    {
        var acquired = false;
        try
        {
            await fanPreviewGate.WaitAsync(lifetime.Token);
            acquired = true;
            FanPreviewButton.IsEnabled = false;
            FanEnabledIcon.Visibility = Visibility.Collapsed;
            ApplyAutomaticButton.Visibility = Visibility.Visible;
            ApplyAutomaticButton.IsEnabled = false;
            FanPreviewText.Text = "检查中…";
            var state = IsFanReaderRunning ? (await ReadControlsAsync()).Automatic!
                : await Task.Run(() => new WindowsAutoFanBackend().Capture(), lifetime.Token);
            fanPreview = AutoFanPolicy.Preview(state, false, DateTimeOffset.UtcNow);
            var temperatureBlocked = state.CpuTemperature is not (> 0 and <= 80) ||
                !new ThermalReading(state.CpuTemperature, state.GpuTemperature, state.GpuPower).GpuWithin(75, false, DateTimeOffset.UtcNow);
            var gpuSource = state.GpuTemperatureSource.StartsWith("nvidia-smi", StringComparison.Ordinal) ? "NVIDIA 驱动只读查询" : "原厂 WMI";
            FanDiagnosticText.Text = $"{state.CapturedAtUtc.ToLocalTime():HH:mm:ss} · 0x{state.Mode:X2} → 0x{fanPreview.ProposedMode:X2}\nCPU {Celsius(state.CpuTemperature)} / GPU {Celsius(state.GpuTemperature)} · {gpuSource}\n" + string.Join("\n", fanPreview.Blockers);
            var alreadyAutomatic = state.Mode == AutoFanPolicy.Automatic && AutoFanPolicy.Blockers(state, false, DateTimeOffset.UtcNow, false).Count == 0;
            FanEnabledIcon.Visibility = alreadyAutomatic ? Visibility.Visible : Visibility.Collapsed;
            ApplyAutomaticButton.Visibility = alreadyAutomatic ? Visibility.Collapsed : Visibility.Visible;
            FanPreviewText.Text = alreadyAutomatic || fanPreview.Eligible ? "" : temperatureBlocked ?
                (state.CpuTemperature is null || state.GpuTemperature is null ? "温度不可用" : "温度过高") : "设备检查未通过";
            ApplyAutomaticButton.IsEnabled = !fanActionBusy && fanPreview.Eligible && !alreadyAutomatic;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { fanPreview = null; FanPreviewText.Text = "检查失败"; ApplyAutomaticButton.IsEnabled = false; FanDiagnosticText.Text = ex.Message; }
        finally { if (acquired) { FanPreviewButton.IsEnabled = true; fanPreviewGate.Release(); } }
    }

    private async void FanPreviewClick(object sender, RoutedEventArgs e) => await PreviewFanAsync();

    private sealed record FanActionResult(bool IsSimulated, string Outcome, string Reason, byte? FinalMode, bool RecoveryVerified, string? RequestId);

    private async void ApplyAutomaticClick(object sender, RoutedEventArgs e)
    {
        if (fanActionBusy || profileActionBusy || cpuPolicyBusy || powerPlanActionBusy || smokeOutput is not null) return;
        if (activeCurveId is not null)
        {
            profileActionBusy = true;
            try { await StopCurveSessionAsync(); await PreviewFanAsync(); }
            catch (Exception ex) { FanActionText.Text = "恢复未确认"; ActionDiagnosticText.Text = ex.Message; }
            finally { profileActionBusy = false; }
            return;
        }
        fanActionBusy = true;
        ApplyAutomaticButton.IsEnabled = false;
        try
        {
            await PreviewFanAsync();
            if (fanPreview is null) { FanActionText.Text = "检查失败"; return; }
            if (fanPreview.State.Mode == AutoFanPolicy.Automatic &&
                AutoFanPolicy.Blockers(fanPreview.State, false, DateTimeOffset.UtcNow, false).Count == 0)
            { FanActionText.Text = "已启用"; return; }
            if (!fanPreview.Eligible) { FanActionText.Text = "暂时无法启用"; return; }
            var helper = Path.Combine(AppContext.BaseDirectory, "helper", "Gs66Control.Helper.exe");
            if (!File.Exists(helper)) { FanActionText.Text = "安装不完整"; return; }
            var requestId = Guid.NewGuid().ToString("N");
            var journal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GS66Control", $"auto-fan-action-{requestId}.json");
            FanActionText.Text = "启用中…";
            var start = new ProcessStartInfo(helper)
            { Arguments = "apply-automatic --confirmed --request " + requestId, UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(helper)! };
            using var process = Process.Start(start) ?? throw new IOException("控制 helper 未能启动。");
            // Process.Start retains the process handle, including the exit code after termination.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
            var text = File.Exists(journal) ? File.ReadAllText(journal) : null;
            var result = text is not null ? JsonSerializer.Deserialize<FanActionResult>(text) : null;
            await PreviewFanAsync();
            if (process.ExitCode == 0 && result is { IsSimulated: false, Outcome: "AutomaticApplied", FinalMode: AutoFanPolicy.Automatic, RecoveryVerified: true } &&
                result.RequestId == requestId && fanPreview?.State.Mode == AutoFanPolicy.Automatic &&
                AutoFanPolicy.Blockers(fanPreview.State, false, DateTimeOffset.UtcNow, false).Count == 0)
                FanActionText.Text = "";
            else { FanActionText.Text = "未确认成功，详见诊断。"; ActionDiagnosticText.Text = result?.Reason ?? "No matching helper result."; }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { FanActionText.Text = "已取消"; }
        catch (TimeoutException) { FanActionText.Text = "等待超时，请刷新状态。"; }
        catch (Exception ex) { FanActionText.Text = "启用失败，详见诊断。"; ActionDiagnosticText.Text = ex.Message; }
        finally { fanActionBusy = false; ApplyAutomaticButton.IsEnabled = fanPreview?.Eligible == true && fanPreview.State.Mode != AutoFanPolicy.Automatic; }
    }

    private async Task VerifyUiAsync()
    {
        try
        {
            await RefreshAsync();
            await PrepareSmokeReaderAsync();
            if (identity?.IsSimulated != false || latest?.IsSimulated != false || latest.CpuLoadPercent is null || latest.MemoryUsedPercent is null ||
                (latest.CpuFanRpm is null && (!latest.FanReadRequiresElevation || EnableFanReadButton.Visibility != Visibility.Visible)) || latest.CpuTemperatureCelsius is null)
                throw new InvalidDataException("Real telemetry or fan binding is unavailable.");
            foreach (ListBoxItem item in Navigation.Items)
            {
                Navigation.SelectedItem = item;
                if (Pages.SelectedIndex.ToString() != item.Tag?.ToString()) throw new InvalidDataException("Navigation did not open its page.");
            }
            Pages.SelectedIndex = 0;
            foreach (var button in new[] { RefreshButton, RecordTuningButton, EnableFanReadButton, ReadDeviceCurveButton, SaveProfileButton, FanPreviewButton, ApplyProfileButton, StopProfileButton, ApplyPerformanceButton, ApplyPowerPlanButton, ApplyCpuPolicyButton, RecoverCurveButton })
                if (string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button)) || button.ToolTip is null)
                    throw new InvalidDataException("An icon action is missing its accessible name or tooltip.");
            NewProfile(); ProfileName.Text = "桌面验证方案";
            SaveProfileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.Load().Profiles.Count != 1) throw new InvalidDataException("Profile save action failed.");
            cpuPoints[1].Temperature.Text = "0";
            if (SaveProfile()) throw new InvalidDataException("Invalid curve was accepted.");
            ProfileList.SelectedIndex = -1; ProfileList.SelectedIndex = 0;
            RefreshInterval.Text = "4"; AccentChoice.SelectedIndex = 1;
            SaveSettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.Load().RefreshSeconds != 4 || store.Load().Accent != "#78A9FF") throw new InvalidDataException("Preferences did not persist.");
            await RefreshAsync();
            if (HistoryLine.Points.Count < 2) throw new InvalidDataException("History did not update.");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            UpdateLayout();
            SaveScreenshot("desktop.png");
            Pages.SelectedIndex = 1;
            if (!await ReadDeviceCurvesAsync() || deviceCurves?.IsSimulated != false || !ReadProfile().Cpu.SequenceEqual(deviceCurves.Cpu) ||
                !ReadProfile().Gpu.SequenceEqual(deviceCurves.Gpu)) throw new InvalidDataException("Read-only device curves were not preserved in the editor.");
            if (!SaveProfile()) throw new InvalidDataException("Device curve draft could not be saved.");
            var shiftBeforeBalance = ReadProfile().PerformanceShift;
            BalanceSlider.Value = 70;
            var balanced = ReadProfile();
            FanCurveControlPolicy.Require(FanCurveControlBaseline.FromSaved(deviceCurves), balanced.Cpu, balanced.Gpu, false, DateTimeOffset.UtcNow);
            if (balanced.BalancePosition != 70 || balanced.PerformanceShift != shiftBeforeBalance || !SaveProfile() || store.Load().Profiles.Last().BalancePosition != 70)
                throw new InvalidDataException("Balance slider did not persist a bounded profile.");
            SelectShift(ProfilePerformanceChoice, 0xC2);
            if (!SaveProfile() || store.Load().Profiles.Last().PerformanceShift != 0xC2 || ReadProfile().BalancePosition is not null)
                throw new InvalidDataException("Profile performance selection did not persist.");
            RecordTuningButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var recordingDeadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (tuningStartBusy && DateTimeOffset.UtcNow < recordingDeadline) await Task.Delay(50);
            if (tuningRecording is null) throw new InvalidDataException("Read-only profile recording did not start.");
            await RefreshAsync();
            FinishTuningRecording();
            if (tuningSummaries.Count != 1 || tuningSummaries[0].IsSimulated || tuningSummaries[0].ReadyToCompare ||
                !File.Exists(Path.Combine(smokeOutput!, "tuning-records.json")))
                throw new InvalidDataException("A short real profile record was not persisted as insufficient for comparison.");
            if (powerPlanSnapshot?.IsSimulated != false || !powerPlanSnapshot.Plans.Any(p => p.SchemeGuid == powerPlanSnapshot.ActiveSchemeGuid))
                throw new InvalidDataException("Native power plan enumeration did not return the active plan.");
            ApplyProfileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ApplyPerformanceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (activeCurveId is not null || profileActionBusy) throw new InvalidDataException("Smoke test started a hardware action.");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            UpdateLayout(); SaveScreenshot("profiles.png");
            Pages.SelectedIndex = 2;
            if (cpuPolicySnapshot?.IsSimulated != false) throw new InvalidDataException("Native CPU policy read failed.");
            var cpuPolicyBeforeSmoke = cpuPolicySnapshot;
            ApplyCpuPolicyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!WindowsCpuPolicyBackend.SamePolicy(cpuPolicyBeforeSmoke, new WindowsCpuPolicyBackend().Capture()))
                throw new InvalidDataException("Read-only smoke changed the CPU policy.");
            CpuPolicyExpander.IsExpanded = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            UpdateLayout(); SaveScreenshot("settings.png");
            Pages.SelectedIndex = 3;
            await PreviewFanAsync();
            if (fanPreview?.State.IsSimulated != false || fanPreview.FirstHardwareWriteApproved || fanPreview.HardwareWrites != 0)
                throw new InvalidDataException("Read-only fan preview failed or hardware write authorization changed.");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            UpdateLayout(); SaveScreenshot("fan-preview.png");
            Pages.SelectedIndex = 4;
            await RefreshPersonalTuningAsync();
            if (PersonalPresets.Length != 3 || string.IsNullOrWhiteSpace(PersonalHealthText.Text))
                throw new InvalidDataException("Personal presets or policy diagnostics are missing.");
            PersonalPresetName.Text = "只读界面验证";
            await SavePersonalPresetAsync();
            if (store.Load().CpuPresets.Count != 1 || PersonalPresets.Length != 4)
                throw new InvalidDataException("Personal preset persistence failed.");
            PersonalApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PersonalUndoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!WindowsCpuPolicyBackend.SamePolicy(cpuPolicyBeforeSmoke, new WindowsCpuPolicyBackend().Capture()) || cpuPolicyBusy)
                throw new InvalidDataException("Read-only personal smoke changed the CPU policy.");
            PersonalPresetChoice.SelectedItem = PersonalPresets.First(p => p.Id == "quiet");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            UpdateLayout(); SaveScreenshot("personal-tuning.png");
            File.WriteAllText(Path.Combine(smokeOutput!, "verification.json"), JsonSerializer.Serialize(new
            { passed = true, identity = identity.Model, telemetry = latest, fanTelemetryPermissionRequired = latest!.FanReadRequiresElevation, autoFanPreview = fanPreview, deviceCurves, cpuPolicySnapshot, savedVendorScenario, personalPresetPersistence = true, personalActionsReadOnly = true, nativeCpuPolicyRead = true, deviceCurveImport = true, profilePersistence = true, balanceProfilePersistence = true, realRecordingPersistence = true, shortRecordingComparisonBlocked = true, nativePowerPlanRead = true, explicitHardwareWrites = 0, invalidCurveRejected = true, settingsPersistence = true, navigation = true, accessibleIconActions = true, historySamples = history.Count }, JsonOptions));
            Exit();
        }
        catch (Exception ex) { FinishSmokeFailure(ex); }
    }

    private void SaveScreenshot(string name)
    {
        var content = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(smokeOutput!, name)); encoder.Save(stream);
        SaveNativeScreenshot(name);
    }

    private void SaveNativeScreenshot(string name)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!IsActive || GetForegroundWindow() != handle || !GetWindowRect(handle, out var bounds)) return;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return;
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new System.Drawing.Size(width, height));
        bitmap.Save(Path.Combine(smokeOutput!, Path.GetFileNameWithoutExtension(name) + "-window.png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    private void FinishSmokeFailure(Exception ex)
    {
        File.WriteAllText(Path.Combine(smokeOutput!, "failure-context.json"), JsonSerializer.Serialize(new
        { model = identity?.Model, telemetry = latest, warning = TelemetryWarningText.Text, actionError = ActionDiagnosticText.Text }, JsonOptions));
        File.WriteAllText(Path.Combine(smokeOutput!, "error.txt"), ex.ToString()); Environment.ExitCode = 1; Exit();
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void HideClick(object sender, RoutedEventArgs e) { if (tray is not null) Hide(); }
    private void ExitClick(object sender, RoutedEventArgs e) => Exit();
    private void NewProfileClick(object sender, RoutedEventArgs e) => NewProfile();
    private void SaveProfileClick(object sender, RoutedEventArgs e) => SaveProfile();
    private void SaveSettingsClick(object sender, RoutedEventArgs e) => SavePreferences();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowBounds bounds);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
