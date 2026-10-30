using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using UsbHub.App.Native;
using UsbHub.Core;

namespace UsbHub.App.Providers;

internal sealed class WinRtBluetoothProvider : IBatteryProvider
{
    public string Name => "Windows 异步蓝牙 GATT";

    public async Task<ProviderResult> ScanAsync(CancellationToken cancellationToken)
    {
        var nodes = NativeDevices.Enumerate().Where(n => n.IsBluetooth).ToArray();
        string[] properties = ["System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected"];
        var endpoints = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), properties,
            DeviceInformationKind.AssociationEndpoint).AsTask(cancellationToken);
        var classic = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), properties,
            DeviceInformationKind.AssociationEndpoint).AsTask(cancellationToken);
        var devices = new List<WirelessDevice>();
        foreach (var entry in endpoints.Select(e => (Endpoint: e, Le: true)).Concat(classic.Select(e => (Endpoint: e, Le: false))))
        {
            var endpoint = entry.Endpoint;
            cancellationToken.ThrowIfCancellationRequested();
            var address = endpoint.Properties.TryGetValue(properties[0], out var rawAddress) ? rawAddress?.ToString()?.Replace(":", "").Replace("-", "") : null;
            var node = !string.IsNullOrEmpty(address) ? nodes.FirstOrDefault(n => n.InstanceId.Contains(address, StringComparison.OrdinalIgnoreCase) || n.Ancestors.Any(a => a.Contains(address, StringComparison.OrdinalIgnoreCase))) : null;
            var connected = endpoint.Properties.TryGetValue(properties[1], out var rawConnected) && rawConnected is bool value ? (bool?)value : null;
            var device = new WirelessDevice
            {
                Id = endpoint.Id, InstanceId = node?.InstanceId ?? endpoint.Id, GroupId = node?.ContainerId ?? (!string.IsNullOrEmpty(address) ? "bluetooth:" + address : endpoint.Id),
                Name = endpoint.Name, Kind = WindowsPropertyProvider.GuessKind(endpoint.Name), Transport = Transport.Bluetooth,
                IsConnected = connected, Source = "蓝牙配对设备", Detail = connected == false ? "设备当前未连接，缓存电量仅供参考。" : "未读取到标准蓝牙电量服务。"
            };
            // Query already connected paired devices; do not wake offline peripherals or initiate pairing.
            if (connected == false || !entry.Le) { devices.Add(device); continue; }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(7));
                using var bluetooth = await BluetoothLEDevice.FromIdAsync(endpoint.Id).AsTask(timeout.Token);
                if (bluetooth is null) { devices.Add(device with { Detail = "Windows 未授予蓝牙设备访问，或设备已断开。" }); continue; }
                var services = await bluetooth.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(timeout.Token);
                try
                {
                    foreach (var service in services.Services)
                    {
                        var characteristics = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached).AsTask(timeout.Token);
                        foreach (var characteristic in characteristics.Characteristics)
                        {
                            var reading = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                            if (reading.Status != GattCommunicationStatus.Success || reading.Value.Length != 1) continue;
                            using var reader = DataReader.FromBuffer(reading.Value);
                            var percent = BatteryDecoder.Percentage(reader.ReadByte());
                            if (!percent.HasValue) continue;
                            device = device with
                            {
                                BatteryPercent = percent, IsConnected = true, ReadAt = DateTimeOffset.Now,
                                Source = "Bluetooth GATT · 180F / 2A19", Detail = "通过 Windows Runtime 直接读取设备 Battery Level 特征。"
                            };
                            break;
                        }
                        if (device.HasBattery) break;
                    }
                    if (!device.HasBattery) device = device with { Detail = "标准蓝牙电量未返回可用值，GATT 状态：" + services.Status };
                }
                finally { foreach (var service in services.Services) service.Dispose(); }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { device = device with { Detail = "蓝牙设备 7 秒内未返回电量，可能休眠或超出范围。" }; }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { device = device with { Detail = "Windows 蓝牙访问失败：" + ex.Message }; }
            devices.Add(device);
        }
        return new ProviderResult(Name, devices);
    }
}
