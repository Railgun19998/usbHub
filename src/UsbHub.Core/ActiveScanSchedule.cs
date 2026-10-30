using System.Text.Json;

namespace UsbHub.Core;

public sealed record ScheduledScanResult(ScanResult Result, bool WasQueried, DateTimeOffset NextQueryAt);
public sealed record ScanCheckpoint(DateTimeOffset? LastAttemptAt = null, ScanResult? Result = null);

public static class ActiveScanSchedule
{
    public const int IntervalSeconds = 1800;

    public static async Task<ScheduledScanResult> ReadAsync(string statePath,
        Func<CancellationToken, Task<ScanResult>> query, DateTimeOffset now, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
        FileStream lease;
        try { lease = new FileStream(statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return Cached(Load(statePath), now, "已有设备读取正在进行，未重复查询。"); }
        using (lease)
        {
            var checkpoint = Load(statePath);
            if (checkpoint.LastAttemptAt is { } last && now < last.AddSeconds(IntervalSeconds))
                return Cached(checkpoint, now);
            // Reserve before touching hardware. Failures and restarts cannot cause rapid retries.
            checkpoint = checkpoint with { LastAttemptAt = now };
            Save(statePath, checkpoint);
            var result = await query(token);
            Save(statePath, checkpoint with { Result = result });
            return new(result, true, now.AddSeconds(IntervalSeconds));
        }
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
            Devices = result.Devices.Select(d => d with { IsCached = d.HasBattery, IsConnected = null }).ToArray(),
            Warnings = warning is null ? result.Warnings : result.Warnings.Append(warning).ToArray()
        };
        return new(result, false, (checkpoint.LastAttemptAt ?? now).AddSeconds(IntervalSeconds));
    }
}
