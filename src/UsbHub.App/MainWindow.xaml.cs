using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        _tray = new TrayIcon(new Guid("b76d7ee4-572b-43ee-a14b-a057f61f2950"), icon, "usbHub · 无线设备电量");
        ConfigureTray(_tray);
        _trayTooltip.OpenRequested += Restore;
        _trayTooltip.RefreshRequested += async () => await RefreshAsync();
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
        _tray?.SetText(summary.Length == 0 ? "usbHub · 未发现设备" : "usbHub\n" + summary);
        if (_trayTooltip.IsVisible) SetTrayDevices();
    }

    internal void Restore() { _trayTooltip.HideImmediately(); Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; if (!_automated) Activate(); }
    private void Exit() { _exit = true; Close(); }

    private async Task RefreshAsync()
    {
        if (_model.IsDemo || _shutdown.IsCancellationRequested) return;
        if (_model.IsScanning) return;
        _model.IsScanning = true;
        _model.Status = "正在读取 Windows、蓝牙和 HID 设备…";
        try
        {
            var scheduled = await DeviceScanner.ScanScheduledAsync(Path.GetDirectoryName(_settingsPath)!, _shutdown.Token);
            var scan = scheduled.Result;
            _model.SetDevices(scan.Devices);
            UpdateTray();
            _model.Notice = string.Join("\n", scan.Warnings.Prepend(_settingsWarning ?? "").Where(s => s.Length > 0));
            _model.Status = scheduled.WasQueried ? $"读取于 {scan.ScannedAt:HH:mm:ss} · 每 30 分钟主动读取" :
                $"使用上次结果 · 下次主动读取 {scheduled.NextQueryAt:HH:mm:ss}（每 30 分钟一次）";
            _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, (scheduled.NextQueryAt - DateTimeOffset.Now).TotalSeconds));
            if (scheduled.WasQueried && _activeSettings.NotificationsEnabled)
            {
                var alerts = _alertPolicy.Evaluate(scan.Devices, _activeSettings.LowBatteryThreshold, DateTimeOffset.Now);
                if (alerts.Count > 0)
                {
                    var body = string.Join("\n", alerts.Select(d => d.Name + "：" + (d.BatteryPercent is int p ? p + "%" : "电量偏低")));
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
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
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
        Notify("usbHub 测试通知", "低电量提醒已准备就绪。这是一条测试消息。");
        _model.Status = "已提交测试通知；显示与否由 Windows 通知 / 勿扰设置决定";
    }
    internal async Task RenderPreviewsAsync(string directory, bool interactiveTrayTest = false)
    {
        Directory.CreateDirectory(directory);
        _model.IsDemo = true; _model.SetDevices(DemoDevices.Create()); _model.Status = "界面体验 · 仅展示示例数据";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        foreach (var page in new[] { "overview", "settings" })
        {
            SetNavigation(page);
            UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, page + ".png")); encoder.Save(file);
        }
        // Exercise actual view-model flows, without hardware writes or notifications.
        _model.SetFilter("bluetooth");
        if (_model.Devices.Count != 3) throw new InvalidOperationException("Bluetooth filter failed");
        _model.SetFilter("low");
        if (_model.Devices.Count != 1) throw new InvalidOperationException("Low-battery filter failed");
        _model.Search = "no-such-device";
        if (!_model.IsEmpty) throw new InvalidOperationException("Empty search state failed");
        _model.Search = ""; _model.SetFilter("all");
        _model.SetFilter("usb");
        if (_model.Devices.Count != 2) throw new InvalidOperationException("USB filter failed");
        _model.SetFilter("all");
        var samplesForPaging = DemoDevices.Create();
        _model.SetDevices(Enumerable.Range(0, 13).Select(i => samplesForPaging[i % samplesForPaging.Count] with { Id = $"page:{i}" }).ToArray());
        if (_model.PageDevices.Count() != 6 || _model.PageCount != 3 || !_model.CanNextPage) throw new InvalidOperationException("First device page failed");
        _model.ChangeDevicePage(2);
        if (_model.PageDevices.Count() != 1 || _model.CanNextPage || !_model.CanPreviousPage) throw new InvalidOperationException("Last device page failed");
        _model.Search = "no-such-device";
        if (_model.PageCount != 1 || _model.PageDevices.Any() || _model.CanPreviousPage) throw new InvalidOperationException("Page reset after search failed");
        _model.Search = ""; _model.SetDevices(samplesForPaging);
        UpdateLayout();
        var cards = FindVisualChildren<GlassCard>(DeviceCards).ToArray();
        if (cards.Length != 6 || cards.Any(card => card.ActualHeight < 260 || card.TranslatePoint(new Point(card.ActualWidth, card.ActualHeight), this).Y > ActualHeight - 36))
            throw new InvalidOperationException("Six-device grid does not fit the window");
        _model.Settings.Save(_settingsPath);
        var reloaded = AppSettings.Load(_settingsPath, out var warning);
        if (warning is not null || reloaded != _model.Settings) throw new InvalidOperationException("Settings roundtrip failed");
        await ValidateWindowRestoreAsync(directory);
        await ValidateTrayAsync(directory, interactiveTrayTest);
        File.WriteAllText(Path.Combine(directory, "ui-smoke.json"), JsonSerializer.Serialize(new { Success = true, Pages = 2, BluetoothFilter = true, UsbFilter = true, LowBatteryFilter = true, EmptySearch = true, DevicePagination = true, SixCardLayout = true, SettingsRoundtrip = true, WindowRestoreCycles = 3, NativeCaptionRemoved = true, TrayBatteryStates = true, TrayRightClickCallbacks = true, TrayHoverIgnored = true, TrayCompactLayout = true, TrayConstrainedViewport = true, TrayMenuActions = true, TrayGlassFallback = true, DeviceTypeThumbnails = 5, InteractiveTrayChecks = interactiveTrayTest ? "passed" : "not requested" }));
        _exit = true; Close();
    }

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private async Task ValidateWindowRestoreAsync(string directory)
    {
        var foreground = GetForegroundWindow();
        var hwnd = new WindowInteropHelper(this).Handle;
        for (var i = 0; i < 3; i++)
        {
            Hide(); Restore();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var style = GetWindowLongPtr(hwnd, -16).ToInt64();
            if ((style & 0x00C00000) != 0 || (style & 0x000F0000) != 0x000F0000 ||
                System.Windows.Shell.WindowChrome.GetWindowChrome(this).GlassFrameThickness != new Thickness(0) || GetForegroundWindow() != foreground)
                throw new InvalidOperationException("Custom caption/restore styles or passive preview failed");
        }
        SetNavigation("overview");
        UpdateLayout();
        var bitmap = new RenderTargetBitmap(144, 48, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(this) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(ActualWidth - 144, 0, 144, 48), Stretch = Stretch.Fill }, null, new Rect(0, 0, 144, 48));
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, "caption-after-restore.png")); encoder.Save(file);
    }

    private async Task ValidateTrayAsync(string directory, bool interactiveTrayTest)
    {
        var samples = DemoDevices.Create();
        var empty = samples[0] with { BatteryPercent = 0 };
        if (new DeviceViewModel(empty, 20).TrayBatteryText != "0%" ||
            new DeviceViewModel(samples[4], 20).TrayBatteryText != "电量未知" ||
            new DeviceViewModel(samples[3], 20).TrayBatteryText != "中电量" ||
            new DeviceViewModel(samples[5], 20).TrayState != "缓存" ||
            new DeviceViewModel(empty with { IsCharging = true }, 20).TrayState != "充电中" ||
            new DeviceViewModel(empty with { IsConnected = false }, 20).TrayState != "已断开")
            throw new InvalidOperationException("Tray battery-state presentation failed");

        _model.Search = "no-such-device";
        SetTrayDevices();
        if (_trayTooltip.DeviceCount != samples.Count) throw new InvalidOperationException("Tray list incorrectly follows main-window search");
        _trayTooltip.ShowForPreview();
        _trayTooltip.SetDevices([samples[0]], 20);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        _trayTooltip.SavePreview(Path.Combine(directory, "tray-battery.png"));
        _trayTooltip.SetDevices(samples, 20);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (_trayTooltip.ActualWidth > 300 || _trayTooltip.ActualHeight > 370)
            throw new InvalidOperationException("Tray compact layout failed");
        _trayTooltip.SavePreview(Path.Combine(directory, "tray-battery-states.png"));
        _trayTooltip.FitToWorkArea(300, 240);
        _trayTooltip.SetDevices(Enumerable.Range(0, 18).Select(i => samples[i % samples.Count] with { Id = $"tray:{i}" }).ToArray(), 20);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        _trayTooltip.UpdateLayout();
        var trayScroll = (ScrollViewer)_trayTooltip.FindName("TrayScroll");
        var trayActions = (FrameworkElement)_trayTooltip.FindName("TrayActions");
        if (_trayTooltip.ActualHeight > 240 || trayScroll.ScrollableHeight <= 0 ||
            trayActions.TranslatePoint(new Point(0, trayActions.ActualHeight), _trayTooltip).Y > _trayTooltip.ActualHeight)
            throw new InvalidOperationException("Constrained tray viewport hides devices or actions");
        trayScroll.ScrollToEnd();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (Math.Abs(trayScroll.VerticalOffset - trayScroll.ScrollableHeight) > 1)
            throw new InvalidOperationException("Constrained tray list cannot reach its last device");
        _trayTooltip.SavePreview(Path.Combine(directory, "tray-constrained.png"));
        _trayTooltip.FitToWorkArea(300, 420);
        _trayTooltip.SetDevices(samples, 20);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        _trayTooltip.SetGlassEnabled(false);
        if (((Border)_trayTooltip.FindName("Frame")).Background is not SolidColorBrush { Color.A: 255 } ||
            ((Border)_trayTooltip.FindName("Highlights")).Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Tray opaque material fallback failed");
        _trayTooltip.SavePreview(Path.Combine(directory, "tray-battery-opaque.png"));
        _trayTooltip.SetGlassEnabled(true);
        _trayTooltip.SetDevices([], 20);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        _trayTooltip.SavePreview(Path.Combine(directory, "tray-empty.png"));
        var opened = 0; var refreshed = 0; var exited = 0;
        _trayTooltip.OpenRequested += () => opened++;
        _trayTooltip.RefreshRequested += () => refreshed++;
        _trayTooltip.ExitRequested += () => exited++;
        foreach (var name in new[] { "OpenButton", "RefreshButton", "ExitButton" })
            ((Button)_trayTooltip.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (opened != 1 || refreshed != 1 || exited != 1 || _trayTooltip.IsVisible)
            throw new InvalidOperationException("Tray panel actions failed");
        _trayTooltip.HideImmediately();
        _model.Search = "";

        using (var icon = DeviceTrayImage.Create("mouse"))
        using (var tray = new TrayIcon(Guid.NewGuid(), icon, "usbHub · 隔离回调验证", registerIcon: false))
        {
            var contextRequests = 0; var openRequests = 0;
            tray.ContextRequested += _ => contextRequests++;
            tray.OpenRequested += () => openRequests++;
            foreach (var callback in new[] { 0x0406, 0x0407, 0x0200, 0x0204, 0x0205 })
                SendMessage(tray.Handle, TrayIcon.CallbackMessage, IntPtr.Zero, new IntPtr(0x00010000 | callback));
            if (contextRequests != 0 || openRequests != 0) throw new InvalidOperationException("Tray hover/button messages opened a panel");
            SendMessage(tray.Handle, TrayIcon.CallbackMessage, IntPtr.Zero, new IntPtr(0x0001007B));
            SendMessage(tray.Handle, TrayIcon.CallbackMessage, IntPtr.Zero, new IntPtr(0x00010401));
            if (contextRequests != 1 || openRequests != 1) throw new InvalidOperationException("Tray right-click/keyboard callbacks failed");
        }

        var icons = new DrawingVisual();
        using (var drawing = icons.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(41, 41, 41)), null, new Rect(0, 0, 240, 48));
            var index = 0;
            foreach (var kind in new[] { "mouse", "keyboard", "headphones", "controller", "device" })
            {
                using var icon = DeviceTrayImage.Create(kind);
                drawing.DrawImage(Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()), new Rect(index++ * 48 + 8, 8, 32, 32));
            }
        }
        var bitmap = new RenderTargetBitmap(240, 48, 96, 96, PixelFormats.Pbgra32); bitmap.Render(icons);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, "tray-icons.png")); encoder.Save(file);
        if (interactiveTrayTest) await ValidateInteractiveTrayAsync();
    }

    private async Task ValidateInteractiveTrayAsync()
    {
        var foreground = GetForegroundWindow();
        using var image = DeviceTrayImage.Create("mouse");
        using var tray = new TrayIcon(Guid.NewGuid(), image, "usbHub · 隔离托盘验证");
        tray.ContextRequested += bounds => _trayTooltip.ShowNear(bounds);
        await Task.Delay(250);
        if (!tray.TryGetBounds(out _)) throw new InvalidOperationException("Tray registration failed; run the UI check in an interactive Windows desktop, outside the restricted sandbox.");
        SendMessage(tray.Handle, TrayIcon.CallbackMessage, IntPtr.Zero, new IntPtr(0x00010406));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (_trayTooltip.IsVisible || GetForegroundWindow() != foreground)
            throw new InvalidOperationException("Tray hover unexpectedly opened/activated a panel");
        _trayTooltip.SetDevices(DemoDevices.Create(), 20);
        SendMessage(tray.Handle, TrayIcon.CallbackMessage, IntPtr.Zero, new IntPtr(0x0001007B));
        if (!_trayTooltip.IsVisible) throw new InvalidOperationException("Tray right-click panel failed");
        SendMessage(tray.Handle, TrayIcon.CallbackMessage, IntPtr.Zero, new IntPtr(0x00010407));
        if (!_trayTooltip.IsVisible) throw new InvalidOperationException("Hover-close dismissed a clicked panel");
        _trayTooltip.HideImmediately();
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
}
