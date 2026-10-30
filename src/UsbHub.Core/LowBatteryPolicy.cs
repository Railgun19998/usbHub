namespace UsbHub.Core;

public sealed class LowBatteryPolicy
{
    private readonly Dictionary<string, DateTimeOffset> _lastSent = new(StringComparer.OrdinalIgnoreCase);
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromHours(4);

    public IReadOnlyList<WirelessDevice> Evaluate(IEnumerable<WirelessDevice> devices, int threshold, DateTimeOffset now)
    {
        var alerts = new List<WirelessDevice>();
        foreach (var device in devices)
        {
            var key = string.IsNullOrEmpty(device.GroupId) ? device.Id : device.GroupId;
            // A small recovery margin avoids repeating alerts around the boundary.
            if (device.IsCharging == true || (!device.IsCached && device.BatteryPercent > threshold + 5) ||
                device.Band is BatteryBand.Medium or BatteryBand.Full)
            {
                _lastSent.Remove(key);
                continue;
            }
            // A PnP cache read or an unknown connection does not prove a currently low battery.
            if (device.IsConnected != true || !device.IsLow(threshold) ||
                device.ReadAt is not { } readAt || now - readAt > TimeSpan.FromMinutes(10)) continue;
            if (_lastSent.TryGetValue(key, out var last) && now - last < Cooldown) continue;
            _lastSent[key] = now;
            alerts.Add(device);
        }
        return alerts;
    }
}
