using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UsbHub.App.Native;
using UsbHub.Core;

namespace UsbHub.App.Providers;

internal sealed class WindowsPropertyProvider : IBatteryProvider
{
    public string Name => "Windows 设备属性";
    public Task<ProviderResult> ScanAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var devices = new List<WirelessDevice>();
        foreach (var node in NativeDevices.Enumerate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Exclude radios, services and host enumerators. Keep the physical Bluetooth endpoint.
            var physical = node.InstanceId.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase) ||
                node.InstanceId.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase);
            if (!node.IsBluetooth || (!physical && node.CachedBattery is null)) continue;
            devices.Add(new WirelessDevice
            {
                Id = node.InstanceId, InstanceId = node.InstanceId, GroupId = node.ContainerId,
                Name = node.Name, Manufacturer = node.Manufacturer, Transport = Transport.Bluetooth, Kind = GuessKind(node.Name),
                BatteryPercent = node.CachedBattery, IsCached = node.CachedBattery.HasValue,
                ReadAt = node.CachedBattery.HasValue ? DateTimeOffset.Now : null,
                Source = node.CachedBattery.HasValue ? "Windows 电量属性（缓存）" : "Windows 设备发现",
                Detail = node.CachedBattery.HasValue ? "由系统驱动维护，更新时间未知；缓存电量仅供参考，不触发低电量提醒。" : "已发现蓝牙设备，尚未读取到标准电量。"
            });
        }
        return new ProviderResult(Name, devices);
    }, cancellationToken);

    internal static string GuessKind(string name)
    {
        if (new[] { "mouse", "鼠标" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) return "mouse";
        if (new[] { "keyboard", "键盘", "air75" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) return "keyboard";
        if (new[] { "head", "耳机", "wh-", "buds", "pelta" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) return "headphones";
        if (new[] { "controller", "gamepad", "手柄", "dualsense", "dualshock" }.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) return "controller";
        return "device";
    }
}

internal sealed class HidBatteryProvider : IBatteryProvider
{
    public string Name => "通用 HID 电量";
    public Task<ProviderResult> ScanAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var devices = new List<WirelessDevice>();
        foreach (var node in NativeDevices.Enumerate(NativeDevices.HidGuid))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.Path is null || (!node.IsBluetooth && !node.Ancestors.Any(id => id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)))) continue;
            var reading = NativeHid.ReadBattery(node.Path);
            var name = string.IsNullOrWhiteSpace(reading.Name) ? node.Name : reading.Name;
            var receiver = new[] { "wireless", "receiver", "dongle", "2.4", "无线", "接收器", "unifying", "bolt" }.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));
            devices.Add(new WirelessDevice
            {
                Id = node.InstanceId, InstanceId = node.InstanceId, GroupId = node.ContainerId,
                Name = name, Manufacturer = string.IsNullOrWhiteSpace(reading.Manufacturer) ? node.Manufacturer : reading.Manufacturer,
                Kind = reading.Kind == "device" ? WindowsPropertyProvider.GuessKind(name) : reading.Kind, Transport = node.IsBluetooth ? Transport.Bluetooth : receiver ? Transport.UsbReceiver : Transport.UsbHid,
                BatteryPercent = reading.Percent, IsConnected = reading.Percent.HasValue ? true : null,
                ReadAt = reading.Percent.HasValue ? DateTimeOffset.Now : null,
                Source = reading.Percent.HasValue ? "HID 标准电量" : "HID 设备发现", Detail = reading.Detail
            });
        }
        return new ProviderResult(Name, devices);
    }, cancellationToken);
}

internal sealed class XInputBatteryProvider : IBatteryProvider
{
    public string Name => "通用 XInput 游戏设备";
    public Task<ProviderResult> ScanAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        var devices = new List<WirelessDevice>();
        for (uint i = 0; i < 4; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capabilitiesResult = XInputGetCapabilities(i, 0, out _);
            var batteryResult = XInputGetBatteryInformation(i, 0, out var battery);
            var device = XInputSupport.Create(i, capabilitiesResult, batteryResult, battery.Type, battery.Level, DateTimeOffset.Now);
            if (device is not null) devices.Add(device);
        }
        return new ProviderResult(Name, devices);
    }, cancellationToken);

    [StructLayout(LayoutKind.Sequential)] private struct BatteryInfo { public byte Type, Level; }
    [StructLayout(LayoutKind.Explicit, Size = 20)] private struct Capabilities { }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetCapabilities(uint index, uint flags, out Capabilities capabilities);
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetBatteryInformation(uint index, byte type, out BatteryInfo information);
}
