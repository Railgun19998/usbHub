using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using UsbHub.App.Native;
using UsbHub.Core;

namespace UsbHub.App.Providers;

internal sealed class DualSenseBatteryProvider : IBatteryProvider
{
    public string Name => "Sony DualSense HID 电量";

    public async Task<ProviderResult> ScanAsync(CancellationToken cancellationToken)
    {
        var nodes = NativeDevices.Enumerate(NativeDevices.HidGuid)
            .Where(n => n.Path is not null && DualSenseBatteryDecoder.Model(n.InstanceId) is not null).ToArray();
        var connectedAddresses = new List<string>();
        if (nodes.Any(n => n.IsBluetooth))
        {
            var endpoints = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                ["System.Devices.Aep.DeviceAddress"], DeviceInformationKind.AssociationEndpoint).AsTask(cancellationToken);
            connectedAddresses.AddRange(endpoints.Select(e => e.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var value)
                ? value?.ToString()?.Replace(":", "").Replace("-", "") ?? "" : "").Where(a => a.Length == 12));
        }
        var devices = new List<WirelessDevice>();
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var caps = NativeHid.Describe(node.Path!);
            // Only Sony's known gamepad collection; never open keyboard, audio or vendor-control streams.
            if (caps is null || caps.UsagePage != 1 || caps.Usage != 5 || caps.InputLength is not (64 or 78)) continue;
            var device = new WirelessDevice
            {
                Id = node.InstanceId, InstanceId = node.InstanceId, GroupId = node.ContainerId,
                Name = DualSenseBatteryDecoder.Model(node.InstanceId)!, Manufacturer = "Sony", Kind = "controller",
                Transport = node.IsBluetooth ? Transport.Bluetooth : Transport.UsbHid, IsBatteryPresent = true,
                Source = "DualSense HID 设备发现", Detail = "尚未收到完整的 DualSense 电量报告。"
            };
            if (node.IsBluetooth && !connectedAddresses.Any(address => node.Ancestors.Prepend(node.InstanceId)
                .Any(id => id.Contains(address, StringComparison.OrdinalIgnoreCase))))
            {
                devices.Add(device with { IsConnected = false, Detail = "Windows 未确认该蓝牙手柄已连接，未发起电量读取。" });
                continue;
            }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(1200));
                using var handle = NativeHid.CreateFile(node.Path!, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (handle.IsInvalid) throw new IOException("手柄输入接口不可读。");
                await using var stream = new FileStream(handle, FileAccess.Read, 1, true);
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    var report = new byte[caps.InputLength];
                    var length = await stream.ReadAsync(report, timeout.Token);
                    if (length == 0) break;
                    var reading = DualSenseBatteryDecoder.Decode(report.AsSpan(0, length), node.IsBluetooth);
                    if (reading is null) continue;
                    device = device with
                    {
                        BatteryRange = reading.Range, IsCharging = reading.IsCharging, IsConnected = true,
                        ReadAt = reading.Range is not null ? DateTimeOffset.Now : null,
                        Source = "DualSense HID · " + (node.IsBluetooth ? "蓝牙 0x31" : "USB 0x01"), Detail = reading.Detail
                    };
                    break;
                }
                if (!device.HasBattery && device.Source == "DualSense HID 设备发现")
                    device = device with { Detail = "未收到完整电量报告；蓝牙简化报告不含电量，保持现有模式，不发送模式切换命令。" };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { device = device with { Detail = "手柄在 1.2 秒内未返回完整电量报告，可能休眠、处于蓝牙简化模式或被占用。" }; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { device = device with { Detail = "手柄电量接口不可读，可能被其他程序独占。" }; }
            devices.Add(device);
        }
        return new ProviderResult(Name, devices);
    }
}
