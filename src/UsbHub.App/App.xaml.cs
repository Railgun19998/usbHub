using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using UsbHub.App.Services;
using UsbHub.App.ViewModels;
using UsbHub.Core;

namespace UsbHub.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _restore;
    private RegisteredWaitHandle? _restoreWait;
    private bool _ownsMutex;
    private string _dataDirectory = "";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _dataDirectory = Argument(e.Args, "--data-dir") ?? AppContext.BaseDirectory;
        DispatcherUnhandledException += (_, args) => { LogError(args.Exception); args.Handled = true; Shutdown(1); };
        try
        {
            if (Argument(e.Args, "--scan-worker") is null)
                _dataDirectory = PortableStorage.Prepare(AppContext.BaseDirectory,
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "usbHub"), Argument(e.Args, "--data-dir"));
            if ((Argument(e.Args, "--scan-worker") ?? Argument(e.Args, "--scan")) is { } output)
            {
                var result = Argument(e.Args, "--scan-worker") is not null
                    ? await DeviceScanner.ScanNativeAsync(CancellationToken.None)
                    : (await DeviceScanner.ScanScheduledAsync(_dataDirectory, CancellationToken.None)).Result;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, DeviceScanner.JsonOptions));
                Shutdown(); return;
            }
#if CHUB_UI_TESTS
            var preview = Argument(e.Args, "--preview");
#else
            string? preview = null;
#endif
            var background = e.Args.Contains("--background", StringComparer.Ordinal);
            if (preview is null)
            {
                _mutex = new Mutex(true, @"Local\UsbHub.Desktop.v1", out _ownsMutex);
                _restore = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\UsbHub.Restore.v1");
                if (!_ownsMutex) { if (!background) _restore.Set(); Shutdown(); return; }
                SetCurrentProcessExplicitAppUserModelID("UsbHub.Desktop");
            }
            var settings = AppSettings.Load(Path.Combine(_dataDirectory, "settings.json"), out var warning);
            var window = new MainWindow(new MainViewModel(settings), Path.Combine(_dataDirectory, "settings.json"), preview is not null, warning);
            MainWindow = window;
            if (preview is not null || background)
            {
                window.ShowActivated = false; window.ShowInTaskbar = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -20000; window.Top = -20000;
            }
            window.Show();
            if (background)
            {
                window.Hide();
                var area = SystemParameters.WorkArea;
                window.Left = area.Left + (area.Width - window.Width) / 2;
                window.Top = area.Top + (area.Height - window.Height) / 2;
                window.ShowInTaskbar = true; window.ShowActivated = true;
            }
            if (_restore is not null) _restoreWait = ThreadPool.RegisterWaitForSingleObject(_restore, (_, _) => Dispatcher.BeginInvoke(window.Restore), null, Timeout.Infinite, false);
#if CHUB_UI_TESTS
            if (preview is not null) await window.RenderPreviewsAsync(preview, e.Args.Contains("--interactive-tray-test", StringComparer.Ordinal));
#endif
        }
        catch (Exception ex) { LogError(ex); Shutdown(1); }
    }

    private void LogError(Exception exception)
    {
        try { Directory.CreateDirectory(_dataDirectory); File.WriteAllText(Path.Combine(_dataDirectory, "last-error.log"), exception.ToString()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _restoreWait?.Unregister(null); _restore?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static string? Argument(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
}
