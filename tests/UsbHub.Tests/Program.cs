using UsbHub.Core;

var checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
var now = DateTimeOffset.UtcNow;
WirelessDevice Device(string id, int? percent = null) => new()
{
    Id = id, Name = id, BatteryPercent = percent, IsConnected = true, ReadAt = now
};

Check(BatteryDecoder.Percentage(0) == 0, "0% is a valid empty battery");
Check(BatteryDecoder.Percentage(100) == 100, "100% is valid");
Check(BatteryDecoder.Percentage(255) is null && BatteryDecoder.Percentage(-1) is null, "invalid/sentinel readings stay unknown");
Check(BatteryDecoder.Normalize(128, 0, 255) == 50, "HID logical range normalizes to percent");
Check(BatteryDecoder.Normalize(256, 0, 255) is null && BatteryDecoder.Normalize(0, 0, 0) is null, "invalid HID ranges never clamp to real batteries");
Check(BatteryDecoder.Capacity(250, 1000) == 25, "absolute capacity uses full-charge denominator");
Check(BatteryDecoder.Capacity(2000, 1000) is null && BatteryDecoder.Capacity(1, 0) is null, "invalid capacity and zero denominator stay unknown");
Check(VendorBatteryDecoder.AsusPelta([0xCC, 0x12, 0x07, 0, 0, 5, 72, 20, 1]) == 72, "Pelta documented battery response decodes");
Check(VendorBatteryDecoder.AsusPelta([0xCC, 0x12, 0x07, 0, 0, 0xFF, 0xAA, 0, 0]) is null, "Pelta firmware NAK is never battery data");
Check(VendorBatteryDecoder.AsusPelta([0xCC, 0x12, 0x08, 0, 0, 5, 72, 20, 1]) is null, "unrelated Pelta commands cannot become battery responses");
Check(VendorBatteryDecoder.AsusPeltaCharging([0xCC, 0x12, 0x08, 0, 0, 1]) == true, "Pelta charging state decodes separately");
Check(VendorBatteryDecoder.MatchesHidpp([0x11, 1, 4, 0x1D, 29, 4, 0], 1, 4, 0x1D), "HID++ matches slot, feature, function and software ID");
Check(!VendorBatteryDecoder.MatchesHidpp([0x11, 2, 4, 0x1D, 29, 4, 0], 1, 4, 0x1D), "another receiver slot cannot be mistaken for our device");
Check(!VendorBatteryDecoder.MatchesHidpp([0x11, 1, 4, 0x1C, 29, 4, 0], 1, 4, 0x1D), "another application's HID++ response is rejected");
var nuphyA = NuPhyCompatibility.Annotate(Device("port-a") with { InstanceId = "HID\\VID_19F5&PID_2620&MI_03\\PORT_A" });
var nuphyB = NuPhyCompatibility.Annotate(Device("port-b") with { InstanceId = "HID\\VID_19F5&PID_2620&MI_03\\PORT_B" });
Check(nuphyA.Name == "NuPhy Air75 V3" && nuphyA.Name == nuphyB.Name && nuphyA.Transport == nuphyB.Transport, "NuPhy detection survives a different USB port path");
Check(!nuphyA.HasBattery, "model recognition is not falsely reported as NuPhy battery support");
Check(NuPhyCompatibility.Annotate(Device("unrelated") with { InstanceId = "HID\\VID_19F5&PID_FFFF" }).Name == "unrelated", "NuPhy adaptation does not apply Air75 claims to other models");

var policy = new LowBatteryPolicy();
Check(policy.Evaluate([Device("mouse", 20)], 20, now).Count == 1, "threshold is inclusive");
Check(policy.Evaluate([Device("mouse", 19)], 20, now.AddMinutes(2)).Count == 0, "repeat scans do not spam alerts");
Check(policy.Evaluate([Device("mouse", 18)], 20, now.AddHours(4)).Count == 0, "stale sample cannot trigger alert");
Check(policy.Evaluate([Device("mouse", 18) with { ReadAt = now.AddHours(4) }], 20, now.AddHours(4)).Count == 1, "fresh sample can remind after cooldown");
policy.Evaluate([Device("mouse", 26)], 20, now.AddHours(4));
Check(policy.Evaluate([Device("mouse", 20)], 20, now.AddHours(4)).Count == 0, "stale sample still suppressed after recovery");
Check(policy.Evaluate([Device("mouse", 20) with { ReadAt = now.AddHours(4) }], 20, now.AddHours(4)).Count == 1, "recovery rearms low-battery alert");
Check(new LowBatteryPolicy().Evaluate([Device("off", 1) with { IsConnected = false }], 20, now).Count == 0, "disconnected devices do not notify");
Check(new LowBatteryPolicy().Evaluate([Device("cached", 1) with { IsCached = true }], 20, now).Count == 0, "Windows cached values do not notify");
Check(new LowBatteryPolicy().Evaluate([Device("unknown-link", 1) with { IsConnected = null }], 20, now).Count == 0, "discovery alone is not a verified connection");
Check(new LowBatteryPolicy().Evaluate([Device("charging", 1) with { IsCharging = true }], 20, now).Count == 0, "charging devices do not notify");
Check(new LowBatteryPolicy().Evaluate([Device("unknown")], 20, now).Count == 0, "unknown battery does not become zero");
Check(new LowBatteryPolicy().Evaluate([Device("pad") with { Band = BatteryBand.Low }], 20, now).Count == 1, "XInput low band triggers without invented percent");
var marginPolicy = new LowBatteryPolicy();
marginPolicy.Evaluate([Device("margin", 20)], 20, now);
marginPolicy.Evaluate([Device("margin", 22)], 20, now.AddMinutes(1));
Check(marginPolicy.Evaluate([Device("margin", 20)], 20, now.AddMinutes(2)).Count == 0, "small oscillations do not rearm notification");

var merged = DeviceMerger.Merge([
    Device("windows", 10) with { GroupId = "same", IsCached = true },
    Device("gatt", 63) with { GroupId = "same" },
    Device("other", 80) with { GroupId = "another", Name = "Identical device name" },
    Device("third", 50) with { GroupId = "third", Name = "Identical device name" }
]);
Check(merged.Count == 3, "same container merges, same names on different devices stay separate");
Check(merged.Single(d => d.GroupId == "same").BatteryPercent == 63, "live protocol reading outranks system cache");
Check(DeviceMerger.Merge([Device("a"), Device("b")]).Count == 2, "missing container IDs do not collapse unrelated devices");
Check(DeviceMerger.Merge([Device("same"), Device("same", 50)]).Count == 1, "identical instance IDs deduplicate without container ID");

var settings = new AppSettings { LowBatteryThreshold = 999, PollIntervalSeconds = 0 }.Validated();
Check(settings.LowBatteryThreshold == 50 && settings.PollIntervalSeconds == 1800, "settings constrain threshold and fix active reads to 30 minutes");
Check(new AppSettings { PollIntervalSeconds = 120 }.Validated().PollIntervalSeconds == 1800, "legacy short intervals migrate to 30 minutes");
var folder = Path.Combine(Path.GetTempPath(), "UsbHub-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var path = Path.Combine(folder, "settings.json");
    settings.Save(path);
    Check(AppSettings.Load(path, out var warning) == settings && warning is null, "settings persist atomically and roundtrip");
    File.WriteAllText(path, "corrupted-json");
    Check(AppSettings.Load(path, out warning) == new AppSettings() && warning is not null, "corrupt settings recover with a visible warning");
    Check(File.ReadAllText(path) == "corrupted-json", "load does not overwrite corrupt user settings");

    var schedulePath = Path.Combine(folder, "scan-state.json");
    var queryCount = 0;
    Task<ScanResult> Query(CancellationToken _) { queryCount++; return Task.FromResult(new ScanResult(now, [Device("scheduled", 0)], [])); }
    var first = await ActiveScanSchedule.ReadAsync(schedulePath, Query, now, CancellationToken.None);
    Check(first.WasQueried && queryCount == 1, "first scheduled batch runs once");
    foreach (var seconds in new[] { 2, 120, 900, 1799 })
    {
        var cached = await ActiveScanSchedule.ReadAsync(schedulePath, Query, now.AddSeconds(seconds), CancellationToken.None);
        Check(!cached.WasQueried && queryCount == 1 && cached.Result.Devices[0].IsCached && cached.Result.Devices[0].BatteryPercent == 0,
            $"repeated/manual/event/restarted reads at {seconds}s use persisted cache");
    }
    var due = await ActiveScanSchedule.ReadAsync(schedulePath, Query, now.AddMinutes(30), CancellationToken.None);
    Check(due.WasQueried && queryCount == 2, "next active batch becomes eligible at exactly 30 minutes");
    try
    {
        await ActiveScanSchedule.ReadAsync(schedulePath, _ => throw new IOException("simulated query failure"), now.AddMinutes(60), CancellationToken.None);
    }
    catch (IOException) { }
    var afterFailure = await ActiveScanSchedule.ReadAsync(schedulePath, Query, now.AddMinutes(60).AddSeconds(1), CancellationToken.None);
    Check(!afterFailure.WasQueried && queryCount == 2, "failed query consumes the interval and cannot retry immediately");
    using (var otherProcess = new FileStream(schedulePath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var concurrent = await ActiveScanSchedule.ReadAsync(schedulePath, Query, now.AddMinutes(90), CancellationToken.None);
        Check(!concurrent.WasQueried && queryCount == 2, "parallel diagnostic/app requests cannot start another active batch");
    }
}
finally
{
    foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
    Directory.Delete(folder);
}
Console.WriteLine($"ALL {checks} CHECKS PASSED");
