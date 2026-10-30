namespace UsbHub.Core;

public enum Transport { Bluetooth, UsbReceiver, UsbHid, WirelessController }
public enum BatteryBand { Empty, Low, Medium, Full }

public sealed record WirelessDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string GroupId { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Kind { get; init; } = "device";
    public Transport Transport { get; init; }
    public int? BatteryPercent { get; init; }
    public BatteryBand? Band { get; init; }
    public bool? IsCharging { get; init; }
    // null means discovery only; presence in PnP is not a connection check.
    public bool? IsConnected { get; init; }
    public bool IsCached { get; init; }
    public DateTimeOffset? ReadAt { get; init; }
    public string Source { get; init; } = "未公开标准电量";
    public string Detail { get; init; } = "设备未公开标准电量字段，可能需要厂商协议适配。";
    public bool HasBattery => BatteryPercent.HasValue || Band.HasValue;
    public bool IsLow(int threshold) => IsConnected != false && !IsCached && IsCharging != true &&
        (BatteryPercent is int p ? p <= threshold : Band is BatteryBand.Empty or BatteryBand.Low);
}

public sealed record ProviderResult(string Provider, IReadOnlyList<WirelessDevice> Devices, string? Error = null);
public sealed record ScanResult(DateTimeOffset ScannedAt, IReadOnlyList<WirelessDevice> Devices, IReadOnlyList<string> Warnings);

public interface IBatteryProvider
{
    string Name { get; }
    Task<ProviderResult> ScanAsync(CancellationToken cancellationToken);
}

public static class DeviceMerger
{
    public static IReadOnlyList<WirelessDevice> Merge(IEnumerable<WirelessDevice> devices) => devices
        // Container IDs identify physical USB/Bluetooth peripherals, not names or VID/PID pairs.
        .GroupBy(d => string.IsNullOrWhiteSpace(d.GroupId) ? d.Id : d.GroupId, StringComparer.OrdinalIgnoreCase)
        .Select(g =>
        {
            var best = g.OrderByDescending(d => d.HasBattery && !d.IsCached)
                .ThenByDescending(d => d.HasBattery).ThenByDescending(d => d.ReadAt).First();
            var named = g.FirstOrDefault(d => d.Kind != "device" && !IsGenericName(d.Name));
            var physical = g.FirstOrDefault(d => d.InstanceId.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase) ||
                d.InstanceId.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase));
            return best with
            {
                Name = physical?.Name ?? (!IsGenericName(best.Name) ? best.Name : named?.Name ?? g.FirstOrDefault(d => !IsGenericName(d.Name))?.Name ?? best.Name),
                Kind = best.Kind != "device" ? best.Kind : g.FirstOrDefault(d => d.Kind != "device")?.Kind ?? "device",
                IsConnected = best.IsConnected ?? g.Select(d => d.IsConnected).FirstOrDefault(c => c.HasValue),
                Detail = best.Detail + string.Concat(g.Where(d => d.Source != best.Source).Select(d => "\n" + d.Source + "：" + d.Detail).Distinct().Take(3))
            };
        }).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    private static bool IsGenericName(string name) => name.Contains("HID", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("符合", StringComparison.Ordinal) || name.Contains("USB Input", StringComparison.OrdinalIgnoreCase);
}
