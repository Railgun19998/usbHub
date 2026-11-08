using System;
using System.IO;
using System.Security;
using Microsoft.Win32;
using UsbHub.Core;

namespace UsbHub.App.Services;

internal sealed class StartupRegistration(string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
{
    private const string ValueName = "CHUB";

    internal string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) as string;
    }

    internal bool IsEnabled => !string.IsNullOrWhiteSpace(ReadCommand());

    internal void Save(AppSettings settings, string settingsPath)
    {
        var command = settings.StartWithWindows
            ? BuildCommand(Environment.ProcessPath ?? throw new IOException("无法确定程序路径，未更改开机启动项。"),
                Path.GetDirectoryName(Path.GetFullPath(settingsPath))!)
            : null;
        var previous = ReadCommand();
        if (string.Equals(previous, command, StringComparison.Ordinal))
        {
            settings.Save(settingsPath);
            return;
        }
        WriteCommand(command);
        try { settings.Save(settingsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { WriteCommand(previous); }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException or SecurityException)
            { throw new IOException("设置保存失败，开机启动项也未能恢复，请检查 Windows 启动应用设置。", new AggregateException(ex, rollback)); }
            throw;
        }
    }

    private void WriteCommand(string? command)
    {
        if (command is null)
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
    }

    internal static string BuildCommand(string processPath, string dataDirectory)
    {
        var command = Quote(Path.GetFullPath(processPath));
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            command += " " + Quote(Path.Combine(AppContext.BaseDirectory, "CHUB.dll"));
        command += " --background";
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)),
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
            command += " --data-dir " + Quote(Path.GetFullPath(dataDirectory));
        if (command.Length > 260)
            throw new IOException("开机启动命令超过 Windows 的 260 字符限制，请将程序或数据移到较短路径后重试。");
        return command;
    }

    // Windows treats trailing backslashes before a closing quote as escape characters.
    private static string Quote(string path) => "\"" + path + new string('\\', path.Length - path.TrimEnd('\\').Length) + "\"";
}
