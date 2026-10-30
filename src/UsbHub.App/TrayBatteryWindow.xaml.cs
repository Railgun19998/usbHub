using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UsbHub.App.Native;
using UsbHub.App.ViewModels;
using UsbHub.Core;

namespace UsbHub.App;

public partial class TrayBatteryWindow : Window
{
    private System.Drawing.Rectangle _anchor;
    private bool _previewMode;
    private bool _glassEnabled = true;
    public static readonly DependencyProperty HighContrastProperty = DependencyProperty.Register(nameof(HighContrast), typeof(bool), typeof(TrayBatteryWindow));
    public bool HighContrast { get => (bool)GetValue(HighContrastProperty); private set => SetValue(HighContrastProperty, value); }
    internal int DeviceCount => DeviceList.Items.Count;
    public event Action? OpenRequested;
    public event Action? RefreshRequested;
    public event Action? ExitRequested;

    public TrayBatteryWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, -20, new IntPtr(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x80)); // TOOLWINDOW
            ApplyMaterial();
        };
        Deactivated += (_, _) => { if (!_previewMode) HideImmediately(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { HideImmediately(); e.Handled = true; } };
    }

    internal void SetDevices(IReadOnlyList<WirelessDevice> devices, int threshold, string emptyText = "未发现设备")
    {
        DeviceList.ItemsSource = devices.Select(d => new DeviceViewModel(d, threshold)).ToArray();
        EmptyLabel.Text = emptyText;
        EmptyLabel.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyMaterial();
        if (IsVisible && !_previewMode) ShowNear(_anchor, false);
    }

    internal void SetGlassEnabled(bool enabled) { _glassEnabled = enabled; ApplyMaterial(); }

    internal void FitToWorkArea(double width, double height)
    {
        MaxWidth = Math.Max(1, Math.Min(520, width));
        Width = MaxWidth;
        MaxHeight = Math.Max(1, Math.Min(420, height));
    }

    private void ApplyMaterial()
    {
        var glass = WindowMaterial.Apply(this, _glassEnabled);
        var contrast = SystemParameters.HighContrast;
        HighContrast = contrast;
        Background = Brushes.Transparent;
        Foreground = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(10, 43, 40));
        Frame.Background = contrast ? SystemColors.WindowBrush : glass ? new LinearGradientBrush(
            new GradientStopCollection { new(Color.FromArgb(235, 255, 255, 255), 0), new(Color.FromArgb(220, 233, 255, 251), 0.45), new(Color.FromArgb(225, 197, 244, 235), 1) }, new Point(0, 0), new Point(0.8, 1)) : new SolidColorBrush(Color.FromRgb(244, 250, 249));
        Frame.BorderBrush = contrast ? SystemColors.WindowTextBrush : new LinearGradientBrush(
            new GradientStopCollection { new(Colors.White, 0), new(Color.FromArgb(100, 167, 209, 197), 0.5), new(Color.FromArgb(180, 190, 231, 220), 1) }, new Point(0, 0), new Point(1, 1));
        Resources["TraySeparator"] = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromArgb(60, 164, 206, 197));
        Highlights.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void ShowNear(System.Drawing.Rectangle icon, bool activate = true)
    {
        _previewMode = false;
        _anchor = icon;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Move to the target monitor before measuring; WPF then uses that monitor's DPI.
        SetWindowPos(hwnd, IntPtr.Zero, icon.Left, icon.Top, 0, 0, 0x15);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = System.Windows.Forms.Screen.FromRectangle(icon).WorkingArea;
        FitToWorkArea((area.Width - 16) / scale, (area.Height - 16) / scale);
        Measure(new Size(Width, MaxHeight));
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Math.Min(DesiredSize.Height, MaxHeight) * scale);
        var x = Math.Clamp(icon.Right - width, area.Left + 8, Math.Max(area.Left + 8, area.Right - width - 8));
        var y = icon.Top - height - 8;
        if (y < area.Top + 8) y = icon.Bottom + 8;
        y = Math.Clamp(y, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - height - 8));
        SetWindowPos(hwnd, new IntPtr(-1), x, y, width, height, 0x10); // TOPMOST, NOACTIVATE
        Show();
        if (activate) { SetForegroundWindow(hwnd); Activate(); }
    }

    internal void HideImmediately() => Hide();

    private void Open_Click(object sender, RoutedEventArgs e) { HideImmediately(); OpenRequested?.Invoke(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) { HideImmediately(); RefreshRequested?.Invoke(); }
    private void Exit_Click(object sender, RoutedEventArgs e) { HideImmediately(); ExitRequested?.Invoke(); }

    internal void ShowForPreview()
    {
        _previewMode = true;
        Topmost = false; ShowActivated = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; Top = -20000;
        Show();
    }

    internal void SavePreview(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
