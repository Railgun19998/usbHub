using System.Text.Json;

namespace UsbHub.Core;

public sealed record AppSettings
{
    public int LowBatteryThreshold { get; init; } = 20;
    public int PollIntervalSeconds { get; init; } = ActiveScanSchedule.IntervalSeconds;
    public bool NotificationsEnabled { get; init; } = true;
    public bool CloseToTray { get; init; } = true;
    public bool GlassEnabled { get; init; } = true;
    public bool StartWithWindows { get; init; }

    public AppSettings Validated() => this with
    {
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 5, 50),
        PollIntervalSeconds = ActiveScanSchedule.IntervalSeconds
    };

    public static AppSettings Load(string path, out string? warning)
    {
        warning = null;
        try
        {
            return File.Exists(path)
                ? (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new()).Validated()
                : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = "设置读取失败，本次使用默认设置：" + ex.Message;
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Validated(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
