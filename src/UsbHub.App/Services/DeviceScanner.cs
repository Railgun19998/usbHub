using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UsbHub.App.Providers;
using UsbHub.Core;

namespace UsbHub.App.Services;

internal static class DeviceScanner
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static async Task<ScheduledScanResult> ScanScheduledAsync(string dataDirectory, CancellationToken token, bool force = false)
    {
        var scheduled = await ActiveScanSchedule.ReadAsync(Path.Combine(dataDirectory, "scan-state.json"), ScanIsolatedAsync, DateTimeOffset.Now, token, force);
        try
        {
            var discovered = PassiveControllerDiscovery.Discover(scheduled.NextQueryAt);
            return scheduled with { Result = scheduled.Result with { Devices = ActiveScanSchedule.FilterDeletedDevices(
                Path.Combine(dataDirectory, "scan-state.json"), ControllerIdentity.WithDiscoveredControllers(scheduled.Result.Devices, discovered)) } };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return scheduled with { Result = scheduled.Result with { Devices = XInputSupport.Consolidate(scheduled.Result.Devices) } };
        }
    }

    internal static async Task<ScanResult> ScanNativeAsync(CancellationToken token)
    {
        IBatteryProvider[] providers = [new WindowsPropertyProvider(), new HidBatteryProvider(), new WinRtBluetoothProvider(), new XInputBatteryProvider(), new DualSenseBatteryProvider(), new VendorBatteryProvider()];
        var results = await Task.WhenAll(providers.Select(async p =>
        {
            try { return await p.ScanAsync(token).WaitAsync(TimeSpan.FromSeconds(20), token); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { return new ProviderResult(p.Name, [], p.Name + "：" + ex.Message); }
        }));
        var all = results.SelectMany(r => r.Devices).ToArray();
        var slottedReceivers = all.Where(d => d.GroupId.Contains(":slot:", StringComparison.Ordinal)).Select(d => d.GroupId.Split(":slot:")[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var merged = DeviceMerger.Merge(all.Where(d => !slottedReceivers.Contains(d.GroupId)))
            .Where(d => d.Transport != Transport.UsbHid || d.Kind != "device" || d.HasBattery).Select(NuPhyCompatibility.Annotate).ToArray();
        return new(DateTimeOffset.Now, XInputSupport.Consolidate(merged),
            results.Where(r => r.Error is not null).Select(r => r.Error!).ToArray());
    }

    private static async Task<ScanResult> ScanIsolatedAsync(CancellationToken token)
    {
        var output = Path.Combine(Path.GetTempPath(), "usbhub-scan-" + Guid.NewGuid().ToString("N") + ".json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }
        };
        // dotnet CHUB.dll and published apphost launches are both supported.
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            process.StartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "CHUB.dll"));
        process.StartInfo.ArgumentList.Add("--scan-worker");
        process.StartInfo.ArgumentList.Add(output);
        var started = false;
        try
        {
            started = process.Start();
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0 || !File.Exists(output)) throw new IOException("设备扫描未完成，请刷新重试。");
            return JsonSerializer.Deserialize<ScanResult>(await File.ReadAllTextAsync(output, token), JsonOptions)
                ?? throw new IOException("扫描结果为空。");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("设备响应超过 40 秒，已结束本次扫描；上次结果仅供参考。"); }
        finally
        {
            if (started && !process.HasExited) process.Kill(true);
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
