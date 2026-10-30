using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using UsbHub.App.Controls;
using UsbHub.Core;

namespace UsbHub.App.ViewModels;

internal sealed class MainViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<WirelessDevice> _devices = [];
    private string _search = "";
    private string _filter = "all";
    private string _page = "overview";
    private string _status = "准备就绪";
    private string _notice = "";
    private bool _isScanning;
    private bool _isDemo;
    private int _devicePage;
    private AppSettings _settings;

    public MainViewModel(AppSettings settings) => _settings = settings;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DeviceViewModel> Devices { get; } = [];
    public IEnumerable<DeviceViewModel> PageDevices => Devices.Skip(_devicePage * 6).Take(6);
    public int PageCount => Math.Max(1, (Devices.Count + 5) / 6);
    public bool HasMultiplePages => PageCount > 1;
    public bool CanPreviousPage => _devicePage > 0;
    public bool CanNextPage => _devicePage + 1 < PageCount;
    public string PageLabel => $"{_devicePage + 1} / {PageCount}";
    public void ChangeDevicePage(int delta) { _devicePage = Math.Clamp(_devicePage + delta, 0, PageCount - 1); PageChanged(); }
    private void PageChanged() { Changed(nameof(PageDevices)); Changed(nameof(HasMultiplePages)); Changed(nameof(CanPreviousPage)); Changed(nameof(CanNextPage)); Changed(nameof(PageLabel)); }
    public IReadOnlyList<WirelessDevice> RawDevices => _devices;
    public AppSettings Settings => _settings;
    public bool IsOverview => _page == "overview";
    public bool IsSettings => _page == "settings";
    public bool IsEmpty => Devices.Count == 0;
    public string EmptyTitle => _devices.Count == 0 ? "等待你的设备加入" : "没有匹配的设备";
    public string EmptyDescription => _devices.Count == 0 ? "连接 USB 接收器，或在 Windows 设置中配对蓝牙设备，然后点击刷新。" : "试试其他设备名称，或切换上方的连接类型。";
    public int TotalCount => _devices.Count;
    public int ReadableCount => _devices.Count(d => d.HasBattery);
    public int LowCount => _devices.Count(d => d.IsLow(Threshold));
    public string ThresholdDescription => $"电量 ≤ {Threshold}% 时提醒";
    public string ProtectionSummary => Notifications ? $"低电量提醒已开启 · 阈值 {Threshold}%" : "低电量提醒已暂停";
    public string RefreshLabel => IsScanning ? "正在读取…" : "刷新设备";
    public bool CanRefresh => !IsScanning && !IsDemo;
    public bool CanEditDevices => !IsScanning;
    public bool IsScanning { get => _isScanning; set { _isScanning = value; Changed(); Changed(nameof(RefreshLabel)); Changed(nameof(CanRefresh)); Changed(nameof(CanEditDevices)); } }
    public bool IsDemo { get => _isDemo; set { _isDemo = value; Changed(); Changed(nameof(CanRefresh)); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string Notice { get => _notice; set { _notice = value; Changed(); Changed(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public string Search { get => _search; set { _search = value; Changed(); ApplyFilter(); } }
    public int Threshold { get => _settings.LowBatteryThreshold; set { _settings = _settings with { LowBatteryThreshold = Math.Clamp(value, 5, 50) }; SettingsChanged(); } }
    public int Interval => ActiveScanSchedule.IntervalSeconds;
    public bool Notifications { get => _settings.NotificationsEnabled; set { _settings = _settings with { NotificationsEnabled = value }; SettingsChanged(); } }
    public bool CloseToTray { get => _settings.CloseToTray; set { _settings = _settings with { CloseToTray = value }; SettingsChanged(); } }
    public bool GlassEnabled { get => _settings.GlassEnabled; set { _settings = _settings with { GlassEnabled = value }; SettingsChanged(); } }

    public void SetPage(string page) { _page = page; Changed(nameof(IsOverview)); Changed(nameof(IsSettings)); }
    public void SetFilter(string filter) { _filter = filter; ApplyFilter(); }
    public void SetDevices(IReadOnlyList<WirelessDevice> devices)
    {
        _devices = devices;
        ApplyFilter();
        Changed(nameof(TotalCount)); Changed(nameof(ReadableCount)); Changed(nameof(LowCount));
    }
    private void SettingsChanged()
    {
        Changed(nameof(Threshold)); Changed(nameof(Interval)); Changed(nameof(Notifications)); Changed(nameof(CloseToTray)); Changed(nameof(GlassEnabled));
        Changed(nameof(ThresholdDescription)); Changed(nameof(ProtectionSummary)); Changed(nameof(LowCount)); Changed(nameof(Settings));
        ApplyFilter();
    }
    private void ApplyFilter()
    {
        Devices.Clear();
        var filtered = _devices.Where(d => (_filter != "bluetooth" || d.Transport == Transport.Bluetooth) &&
            (_filter != "usb" || d.Transport is Transport.UsbHid or Transport.UsbReceiver or Transport.WirelessController) &&
            (_filter != "low" || d.IsLow(Threshold)) &&
            (string.IsNullOrWhiteSpace(Search) || d.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase) || d.Manufacturer.Contains(Search, StringComparison.CurrentCultureIgnoreCase)));
        foreach (var d in filtered.OrderByDescending(d => d.IsLow(Threshold)).ThenByDescending(d => d.HasBattery)) Devices.Add(new(d, Threshold));
        _devicePage = 0;
        PageChanged();
        Changed(nameof(IsEmpty)); Changed(nameof(EmptyTitle)); Changed(nameof(EmptyDescription));
    }
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}

internal sealed class DeviceViewModel(WirelessDevice device, int threshold)
{
    public WirelessDevice Device => device;
    public string Name => device.Name;
    public string Kind => ControllerIdentity.IllustrationKind(device);
    public ImageSource TrayTypeImage => DeviceTrayImage.CreateSource(Kind);
    public string TransportLabel => device.Transport switch { Transport.Bluetooth => "蓝牙", Transport.UsbReceiver => "USB 接收器", Transport.WirelessController => device.IsBatteryPresent == false ? "有线手柄" : "XInput 手柄", _ => device.Kind == "controller" ? "USB 手柄" : "USB HID · 类型待确认" };
    public string Icon => device.Transport == Transport.Bluetooth ? "\uE702" : "\uE88E";
    public string BatteryText => device.BatteryPercent is int p ? p.ToString() : device.BatteryRange is { IsValid: true } range ? range.Minimum == range.Maximum ? range.Minimum.ToString() : $"{range.Minimum}–{range.Maximum}" : device.Band switch { BatteryBand.Empty => "空", BatteryBand.Low => "低", BatteryBand.Medium => "中", BatteryBand.Full => "满", _ => "—" };
    public string BatteryUnit => device.BatteryPercent.HasValue || device.BatteryRange is { IsValid: true } ? "%" : device.Band.HasValue ? "电量等级" : device.IsBatteryPresent == false ? "有线供电" : "电量未知";
    public string TrayBatteryText => device.BatteryPercent.HasValue || device.BatteryRange is { IsValid: true } ? $"{BatteryText}%" : device.Band.HasValue ? $"{BatteryText}电量" : device.IsBatteryPresent == false ? "有线供电" : "电量未知";
    public string TrayState => device.IsConnected == false ? device.IsCached ? "已断开 · 缓存" : "已断开" :
        device.IsCached ? "缓存" : device.IsCharging == true ? "充电中" : device.IsConnected is null ? "连接待确认" : "";
    public double BatteryWidth => BatteryPercent * 1.4;
    public bool ShowBar => device.BatteryPercent.HasValue || device.BatteryRange is { IsValid: true };
    public double BatteryPercent => device.BatteryPercent ?? (device.BatteryRange is { IsValid: true } range ? range.Minimum : 0);
    public bool IsBatteryUnknown => !device.HasBattery;
    public bool NeedsAttention => device.IsLow(threshold) && device.IsCharging != true;
    public Brush BarColor => System.Windows.SystemParameters.HighContrast ? System.Windows.SystemColors.HighlightBrush : new LinearGradientBrush(NeedsAttention ? System.Windows.Media.Color.FromRgb(235, 145, 32) : System.Windows.Media.Color.FromRgb(0, 160, 139), NeedsAttention ? System.Windows.Media.Color.FromRgb(255, 191, 100) : System.Windows.Media.Color.FromRgb(18, 206, 176), 0);
    public string Status => device.IsConnected == false ? device.IsCached ? "已断开 · 缓存电量" : "已断开" : device.IsCached ? "系统缓存" : device.IsCharging == true ? "正在充电" :
        device.IsBatteryPresent == false ? "有线供电" : device.IsLow(threshold) ? "需要充电" : device.HasBattery ? "电量正常" : "等待电量信息";
    public string Source => device.Source;
    public string Detail => device.Detail + (device.ReadAt is { } time ? $"\n读取于 {time:HH:mm:ss}（非设备内部更新时间）" : "") +
        (device.Transport == Transport.UsbHid ? "\nUSB HID 可能是有线设备；仅凭 HID 不能确认无线频段。" : "");
    public Brush Color => System.Windows.SystemParameters.HighContrast ? System.Windows.SystemColors.WindowTextBrush : new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(
        !device.HasBattery ? "#678783" : NeedsAttention ? "#BE751B" : "#0A2B28"));
}
