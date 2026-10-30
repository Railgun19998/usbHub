using System.Text.Json;

namespace UsbHub.Core;

public sealed record ScheduledScanResult(ScanResult Result, bool WasQueried, DateTimeOffset NextQueryAt);
public sealed record ScanCheckpoint(DateTimeOffset? LastAttemptAt = null, ScanResult? Result = null,
    IReadOnlyList<string>? DeletedDeviceKeys = null);

public static class ActiveScanSchedule
{
    public const int IntervalSeconds = 1800;

    public static async Task<ScheduledScanResult> ReadAsync(string statePath,
        Func<CancellationToken, Task<ScanResult>> query, DateTimeOffset now, CancellationToken token, bool force = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
        FileStream lease;
        try { lease = new FileStream(statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return Cached(Load(statePath), now, "已有设备读取正在进行，未重复查询。"); }
        using (lease)
        {
            var checkpoint = Load(statePath);
            if (!force && checkpoint.LastAttemptAt is { } last && now < last.AddSeconds(IntervalSeconds))
                return Cached(checkpoint, now);
            // Reserve automatic reads before hardware access. Explicit manual refresh may bypass cooldown.
            checkpoint = checkpoint with { LastAttemptAt = now };
            Save(statePath, checkpoint);
            var result = await query(token);
            result = result with { Devices = FilterDeletedDevices(checkpoint, result.Devices) };
            Save(statePath, checkpoint with { Result = result });
            return new(result, true, now.AddSeconds(IntervalSeconds));
        }
    }

    // Keep removal markers in the checkpoint so refresh/restart cannot resurrect a deleted record.
    // The query timestamp is preserved: editing records never starts a hardware read.
    public static void ForgetDevice(string statePath, WirelessDevice device)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
        using var lease = new FileStream(statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var checkpoint = Load(statePath);
        checkpoint = checkpoint with
        {
            DeletedDeviceKeys = (checkpoint.DeletedDeviceKeys ?? []).Append(DeviceKey(device))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
        if (checkpoint.Result is { } result)
            checkpoint = checkpoint with { Result = result with { Devices = FilterDeletedDevices(checkpoint, result.Devices) } };
        Save(statePath, checkpoint);
    }

    public static void RestoreDeletedDevices(string statePath)
    {
        if (!File.Exists(statePath)) return;
        using var lease = new FileStream(statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Save(statePath, Load(statePath) with { DeletedDeviceKeys = null });
    }

    public static IReadOnlyList<WirelessDevice> FilterDeletedDevices(string statePath, IEnumerable<WirelessDevice> devices) =>
        FilterDeletedDevices(Load(statePath), devices);

    private static string DeviceKey(WirelessDevice device) => string.IsNullOrWhiteSpace(device.GroupId)
        ? "id:" + device.Id : "group:" + device.GroupId;

    private static IReadOnlyList<WirelessDevice> FilterDeletedDevices(ScanCheckpoint checkpoint, IEnumerable<WirelessDevice> devices)
    {
        var removed = (checkpoint.DeletedDeviceKeys ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return devices.Where(d => !removed.Contains(DeviceKey(d))).ToArray();
    }

    private static ScanCheckpoint Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<ScanCheckpoint>(File.ReadAllText(path)) ?? throw new IOException("设备读取计划为空；为避免频繁查询，已停止本次读取。")
        : new();

    private static void Save(string path, ScanCheckpoint checkpoint)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(checkpoint));
        File.Move(temporary, path, true);
    }

    private static ScheduledScanResult Cached(ScanCheckpoint checkpoint, DateTimeOffset now, string? warning = null)
    {
        var result = checkpoint.Result ?? new ScanResult(now, [], []);
        result = result with
        {
            Devices = FilterDeletedDevices(checkpoint, result.Devices).Select(d => d with { IsCached = d.HasBattery, IsConnected = null }).ToArray(),
            Warnings = warning is null ? result.Warnings : result.Warnings.Append(warning).ToArray()
        };
        return new(result, false, (checkpoint.LastAttemptAt ?? now).AddSeconds(IntervalSeconds));
    }
}
