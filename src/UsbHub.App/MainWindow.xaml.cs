using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using UsbHub.App.Controls;
using UsbHub.App.Native;
using UsbHub.App.Services;
using UsbHub.App.ViewModels;
using UsbHub.Core;

namespace UsbHub.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private readonly string _settingsPath;
    private readonly bool _automated;
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _plugDebounce = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource _shutdown = new();
    private readonly LowBatteryPolicy _alertPolicy = new();
    private TrayIcon? _tray;
    private readonly TrayBatteryWindow _trayTooltip = new();
    private bool _exit;
    private string? _settingsWarning;
    private AppSettings _activeSettings;
    private bool _maximizePressed;

    internal MainWindow(MainViewModel model, string settingsPath, bool automated, string? warning)
    {
        _model = model; _settingsPath = settingsPath; _automated = automated; _settingsWarning = warning; _activeSettings = model.Settings;
        GlassEffects.MotionEnabled = !automated;
        InitializeComponent();
        VersionLabel.Text = $"WINDOWS 11  /  ver{typeof(App).Assembly.GetName().Version!.ToString(3)}";
        DataContext = model;
        SetNavigation("overview");
        SourceInitialized += (_, _) =>
        {
            ApplyMaterial();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        };
        _timer.Interval = TimeSpan.FromSeconds(model.Interval);
        _timer.Tick += async (_, _) => await RefreshAsync();
        _plugDebounce.Tick += async (_, _) => { _plugDebounce.Stop(); await RefreshAsync(); };
        Loaded += async (_, _) =>
        {
            if (_automated) return;
            CreateTray();
            _timer.Start();
            await RefreshAsync();
        };
        SystemParameters.StaticPropertyChanged += SystemPreferenceChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _shutdown.Cancel(); _timer.Stop(); _plugDebounce.Stop(); _tray?.Dispose();
            _trayTooltip.Close();
            SystemParameters.StaticPropertyChanged -= SystemPreferenceChanged;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
            Application.Current.Shutdown();
        };
    }

    private void CreateTray()
    {
        if (_tray is not null) return;
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/UsbHub.ico"))!;
        using var stream = resource.Stream;
        using var icon = new System.Drawing.Icon(stream);
        _tray = new TrayIcon(new Guid("b76d7ee4-572b-43ee-a14b-a057f61f2950"), icon, "CHUB · 无线设备电量");
        ConfigureTray(_tray);
        _trayTooltip.OpenRequested += Restore;
        _trayTooltip.RefreshRequested += async () => await RefreshAsync(force: true);
        _trayTooltip.ExitRequested += Exit;
    }

    private void ConfigureTray(TrayIcon tray)
    {
        tray.OpenRequested += Restore;
        tray.ContextRequested += rectangle =>
        {
            SetTrayDevices();
            _trayTooltip.ShowNear(rectangle);
        };
    }

    private void SetTrayDevices() => _trayTooltip.SetDevices(
        _model.RawDevices,
        _activeSettings.LowBatteryThreshold, _model.IsScanning ? "正在读取设备电量…" : "未发现设备");

    private void UpdateTray()
    {
        if (_automated) return;
        var summary = string.Join("\n", _model.RawDevices.Select(d => $"{d.Name}：{new DeviceViewModel(d, _activeSettings.LowBatteryThreshold).TrayBatteryText}"));
        _tray?.SetText(summary.Length == 0 ? "CHUB · 未发现设备" : "CHUB\n" + summary);
        if (_trayTooltip.IsVisible) SetTrayDevices();
    }

    internal void Restore() { _trayTooltip.HideImmediately(); Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; if (!_automated) Activate(); }
    private void Exit() { _exit = true; Close(); }

    private async Task RefreshAsync(bool force = false)
    {
        if (_model.IsDemo || _shutdown.IsCancellationRequested) return;
        if (_model.IsScanning) return;
        _model.IsScanning = true;
        _model.Status = "正在读取 Windows、蓝牙和 HID 设备…";
        try
        {
            var scheduled = await DeviceScanner.ScanScheduledAsync(Path.GetDirectoryName(_settingsPath)!, _shutdown.Token, force);
            var scan = scheduled.Result;
            _model.SetDevices(scan.Devices);
            UpdateTray();
            _model.Notice = string.Join("\n", scan.Warnings.Prepend(_settingsWarning ?? "").Where(s => s.Length > 0));
            _model.Status = scheduled.WasQueried ? $"读取于 {scan.ScannedAt:HH:mm:ss} · 自动读取间隔 30 分钟" :
                $"已更新设备列表 · 下次自动读取电量 {scheduled.NextQueryAt:HH:mm:ss} · 可手动立即刷新";
            _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, (scheduled.NextQueryAt - DateTimeOffset.Now).TotalSeconds));
            if (scheduled.WasQueried && _activeSettings.NotificationsEnabled)
            {
                var alerts = _alertPolicy.Evaluate(scan.Devices, _activeSettings.LowBatteryThreshold, DateTimeOffset.Now);
                if (alerts.Count > 0)
                {
                    var body = string.Join("\n", alerts.Select(d => d.Name + "：" + new DeviceViewModel(d, _activeSettings.LowBatteryThreshold).TrayBatteryText));
                    Notify("设备电量偏低", body);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or JsonException or Win32Exception)
        {
            // Previous values may no longer describe connected devices after a failed scan.
            _model.SetDevices(_model.RawDevices.Select(d => d with { IsCached = d.HasBattery, IsConnected = null }).ToArray());
            UpdateTray();
            _model.Notice = ex.Message;
            _model.Status = "刷新失败 · 保留上次结果（已标为缓存）";
        }
        finally
        {
            _model.IsScanning = false;
        }
    }

    private void Notify(string title, string body)
    {
        if (_model.IsDemo || _automated) return;
        _tray?.ShowNotification(title, body);
    }

    private void ApplyMaterial()
    {
        WindowMaterial.Apply(this, _activeSettings.GlassEnabled);
        _trayTooltip.SetGlassEnabled(_activeSettings.GlassEnabled);
        var contrast = SystemParameters.HighContrast;
        GlassEffects.MotionEnabled = !_automated && _activeSettings.GlassEnabled && !contrast;
        Atmosphere.Opacity = _activeSettings.GlassEnabled && WindowMaterial.TransparencyEnabled() && !contrast ? 1 : 0;
        Atmosphere.Visibility = Atmosphere.Opacity == 0 ? Visibility.Collapsed : Visibility.Visible;
        WindowBorder.Background = Atmosphere.Opacity == 0 ? contrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(244, 250, 249)) : Brushes.Transparent;
        var resources = Application.Current.Resources;
        resources["TextPrimary"] = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(10, 43, 40));
        resources["TextSecondary"] = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(103, 135, 131));
        resources["Surface"] = contrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromArgb(184, 255, 255, 255));
        resources["SurfaceStroke"] = contrast ? SystemColors.WindowTextBrush : new LinearGradientBrush(Colors.White, Color.FromArgb(150, 164, 220, 212), 45);
        resources["Accent"] = contrast ? SystemColors.HighlightBrush : new SolidColorBrush(Color.FromRgb(18, 194, 168));
        resources["AccentGradient"] = contrast ? SystemColors.HighlightBrush : new LinearGradientBrush(Color.FromRgb(18, 194, 168), Color.FromRgb(8, 126, 114), 45);
        resources["InputSurface"] = contrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromArgb(184, 255, 255, 255));
        resources["MenuSurface"] = contrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(244, 250, 249));
        resources["SelectionSurface"] = contrast ? SystemColors.HighlightBrush : new SolidColorBrush(Color.FromArgb(32, 18, 194, 168));
        resources["SelectionText"] = contrast ? SystemColors.HighlightTextBrush : resources["TextPrimary"];
        resources["CardBackground"] = contrast ? SystemColors.WindowBrush : new LinearGradientBrush(
            new GradientStopCollection { new(Color.FromArgb(236, 255, 255, 255), 0), new(Color.FromArgb(184, 255, 255, 255), 0.58), new(Color.FromArgb(128, 215, 249, 243), 1) }, new Point(0, 0), new Point(0.85, 1));
    }

    private void SystemPreferenceChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(ApplyMaterial);
    private void UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(ApplyMaterial);
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        // HTMAXBUTTON lets Windows 11 show its native Snap Layout flyout on hover.
        if (msg == 0x0084 && MaximizeButton.IsVisible)
        {
            var point = new Point(unchecked((short)(lparam.ToInt64() & 0xFFFF)), unchecked((short)((lparam.ToInt64() >> 16) & 0xFFFF)));
            var relative = MaximizeButton.PointFromScreen(point);
            if (relative.X >= 0 && relative.Y >= 0 && relative.X < MaximizeButton.ActualWidth && relative.Y < MaximizeButton.ActualHeight)
            { handled = true; return new IntPtr(9); }
        }
        if (msg == 0x00A1 && wparam.ToInt64() == 9) { _maximizePressed = true; handled = true; }
        if (msg == 0x00A2 && wparam.ToInt64() == 9 && _maximizePressed)
        { _maximizePressed = false; Maximize_Click(this, new RoutedEventArgs()); handled = true; }
        if (msg == 0x0219 || (msg == 0x0218 && wparam.ToInt64() is 7 or 18)) { _plugDebounce.Stop(); _plugDebounce.Start(); }
        if (msg == 0x001A) Dispatcher.BeginInvoke(ApplyMaterial);
        return IntPtr.Zero;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exit && !_automated && _activeSettings.CloseToTray && _tray is not null) { e.Cancel = true; Hide(); }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync(force: true);
    private void Navigate_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string page }) SetNavigation(page); }
    private void SetNavigation(string page)
    {
        _model.SetPage(page);
        foreach (var button in new[] { OverviewNav, SettingsNav })
        {
            var selected = (string)button.Tag == page;
            button.Background = selected ? (Brush)FindResource("AccentGradient") : Brushes.Transparent;
            button.Foreground = selected ? Brushes.White : (Brush)FindResource("TextSecondary");
        }
        PageTitle.Text = page == "settings" ? "提醒与设置" : "设备总览";
    }
    private void PreviousPage_Click(object sender, RoutedEventArgs e) => _model.ChangeDevicePage(-1);
    private void NextPage_Click(object sender, RoutedEventArgs e) => _model.ChangeDevicePage(1);
    private string ScanStatePath => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "scan-state.json");
    private void DeleteDevice_Click(object sender, RoutedEventArgs e)
    {
        if (!_model.CanEditDevices || sender is not MenuItem { DataContext: DeviceViewModel device }) return;
        try
        {
            ActiveScanSchedule.ForgetDevice(ScanStatePath, device.Device);
            _model.SetDevices(ActiveScanSchedule.FilterDeletedDevices(ScanStatePath, _model.RawDevices));
            SetTrayDevices(); UpdateTray();
            _model.Status = $"已删除 {device.Name} 的信息 · 可在设置中恢复识别";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { _model.Notice = "删除失败，请在设备读取结束后重试：" + ex.Message; }
    }
    private void RestoreDeletedDevices_Click(object sender, RoutedEventArgs e)
    {
        if (!_model.CanEditDevices) return;
        try
        {
            ActiveScanSchedule.RestoreDeletedDevices(ScanStatePath);
            _model.Status = "已恢复设备识别 · 点击刷新设备重新读取";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { _model.Notice = "恢复失败，请在设备读取结束后重试：" + ex.Message; }
    }
    private void Filter_Checked(object sender, RoutedEventArgs e) { if (DataContext is MainViewModel model && sender is RadioButton { Tag: string filter }) model.SetFilter(filter); }
    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _model.Settings.Save(_settingsPath);
            _activeSettings = _model.Settings;
            _settingsWarning = null;
            _timer.Interval = TimeSpan.FromSeconds(_model.Interval);
            ApplyMaterial();
            _model.Notice = "";
            _model.Status = "设置已保存";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _model.Notice = "设置保存失败：" + ex.Message; }
    }
    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        if (_model.IsDemo) { _model.Status = "界面体验模式不发送系统通知"; return; }
        Notify("CHUB 测试通知", "低电量提醒已准备就绪。这是一条测试消息。");
        _model.Status = "已提交测试通知；显示与否由 Windows 通知 / 勿扰设置决定";
    }
}
