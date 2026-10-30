using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UsbHub.App.Native;
using UsbHub.Core;

namespace UsbHub.App.Providers;

internal sealed class VendorBatteryProvider : IBatteryProvider
{
    public string Name => "已适配的厂商电量协议";

    public async Task<ProviderResult> ScanAsync(CancellationToken cancellationToken)
    {
        var devices = new List<WirelessDevice>();
        var warnings = new List<string>();
        var interfaces = NativeDevices.Enumerate(NativeDevices.HidGuid);
        foreach (var node in interfaces)
        {
            if (node.Path is null) continue;
            var pelta = node.Path.Contains("vid_0b05&pid_1b84", StringComparison.OrdinalIgnoreCase);
            var logitech = node.Path.Contains("vid_046d&pid_c547", StringComparison.OrdinalIgnoreCase);
            if (!pelta && !logitech) continue;
            var caps = NativeHid.Describe(node.Path);
            if (caps is null || caps.UsagePage < 0xFF00 || caps.InputLength is < 7 or > 1024 || caps.OutputLength > 1024) continue;
            if (pelta && (caps.UsagePage != 0xFF00 || caps.OutputLength < 64)) continue;
            if (logitech && (caps.UsagePage != 0xFF00 || caps.InputLength < 20 || caps.OutputLength < 20)) continue;
            try
            {
                using var channel = new HidQueryChannel(node.Path, caps);
                if (pelta)
                {
                    var response = await channel.QueryAsync([0xCC, 0x12, 0x07], r => r.Length >= 9 && r[0] == 0xCC && r[1] == 0x12 && r[2] == 0x07, cancellationToken);
                    var percent = response is null ? null : VendorBatteryDecoder.AsusPelta(response);
                    if (!percent.HasValue) continue;
                    bool? charging = null;
                    try
                    {
                        var charge = await channel.QueryAsync([0xCC, 0x12, 0x08], r => r.Length >= 6 && r[0] == 0xCC && r[1] == 0x12 && r[2] == 0x08, cancellationToken);
                        charging = charge is null ? null : VendorBatteryDecoder.AsusPeltaCharging(charge);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                    devices.Add(new WirelessDevice
                    {
                        Id = node.InstanceId, InstanceId = node.InstanceId, GroupId = node.ContainerId, Name = "ROG PELTA", Manufacturer = "ASUS", Kind = "headphones",
                        Transport = Transport.UsbReceiver, BatteryPercent = percent, IsCharging = charging, IsConnected = true, ReadAt = DateTimeOffset.Now,
                        Source = "ROG PELTA · 厂商 HID 查询", Detail = "已匹配 0B05:1B84；仅读取电量与充电状态。"
                    });
                }
                else
                {
                    // C547 supports up to two paired devices. Never merge different receiver slots.
                    for (byte slot = 1; slot <= 2; slot++)
                    {
                        try
                        {
                            var d = await ReadLogitechAsync(channel, node, slot, cancellationToken);
                            if (d is not null) devices.Add(d);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                warnings.Add((pelta ? "ROG PELTA" : "Logitech HID++") + " 查询未完成：设备可能休眠、断开或被占用。");
            }
        }
        return new ProviderResult(Name, devices, warnings.Count > 0 ? string.Join("\n", warnings.Distinct()) : null);
    }

    private static async Task<WirelessDevice?> ReadLogitechAsync(HidQueryChannel channel, NativeDevices.Node node, byte slot, CancellationToken token)
    {
        byte softwareId = (byte)Random.Shared.Next(1, 15);
        async Task<byte[]?> Query(byte feature, byte function, params byte[] parameters)
        {
            softwareId = (byte)(softwareId % 15 + 1);
            var functionAndId = (byte)((function << 4) | softwareId);
            var request = new byte[20]; request[0] = 0x11; request[1] = slot; request[2] = feature; request[3] = functionAndId;
            parameters.CopyTo(request, 4);
            var result = await channel.QueryAsync(request, r => VendorBatteryDecoder.MatchesHidpp(r, slot, feature, functionAndId) ||
                (r.Length >= 7 && r[1] == slot && r[2] == 0xFF && r[3] == feature && r[4] == functionAndId), token);
            return result is { Length: >= 7 } && result[2] != 0xFF ? result[4..] : null;
        }
        async Task<byte> Feature(ushort id) => (await Query(0, 0, (byte)(id >> 8), (byte)id)) is { Length: > 0 } r ? r[0] : (byte)0;
        var unified = await Feature(0x1004);
        int? percent = null; BatteryBand? band = null; bool? charging = null;
        if (unified != 0)
        {
            var caps = await Query(unified, 0);
            var battery = await Query(unified, 1);
            if (battery is not { Length: >= 4 }) return null;
            // Capability bit 1 indicates a reported state-of-charge percentage.
            if (caps is { Length: >= 2 } && (caps[1] & 2) != 0) percent = BatteryDecoder.Percentage(battery[0]);
            else band = battery[1] switch { 1 => BatteryBand.Empty, 2 => BatteryBand.Low, 4 => BatteryBand.Medium, 8 => BatteryBand.Full, _ => null };
            charging = battery[2] is 1 or 2 or 3;
        }
        else
        {
            var legacy = await Feature(0x1000);
            if (legacy == 0) return null;
            var battery = await Query(legacy, 0);
            if (battery is not { Length: >= 3 }) return null;
            percent = BatteryDecoder.Percentage(battery[0]);
            charging = battery[2] is 1 or 2 or 3;
        }
        if (!percent.HasValue && !band.HasValue) return null;
        var name = $"Logitech 无线设备 {slot}";
        var kind = "device";
        var nameFeature = await Feature(0x0005);
        if (nameFeature != 0)
        {
            var length = (await Query(nameFeature, 0))?[0] ?? 0;
            if (length is > 0 and < 100)
            {
                var bytes = new List<byte>();
                while (bytes.Count < length)
                {
                    var part = await Query(nameFeature, 1, (byte)bytes.Count);
                    if (part is null || part.Length == 0) break;
                    bytes.AddRange(part.Take(length - bytes.Count));
                }
                if (bytes.Count == length) name = Encoding.UTF8.GetString(bytes.ToArray());
            }
            var type = await Query(nameFeature, 2);
            if (type is { Length: > 0 }) kind = type[0] switch { 0 => "keyboard", 3 => "mouse", _ => "device" };
        }
        return new WirelessDevice
        {
            Id = node.InstanceId + ":slot:" + slot, InstanceId = node.InstanceId,
            GroupId = (string.IsNullOrEmpty(node.ContainerId) ? node.InstanceId : node.ContainerId) + ":slot:" + slot,
            Name = name, Manufacturer = "Logitech", Kind = kind, Transport = Transport.UsbReceiver,
            BatteryPercent = percent, Band = band, IsCharging = charging, IsConnected = true, ReadAt = DateTimeOffset.Now,
            Source = "Logitech HID++ · " + (unified != 0 ? "1004" : "1000"), Detail = $"046D:C547 接收器槽位 {slot}；仅查询功能表、名称与电量。"
        };
    }
}
