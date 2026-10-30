using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using UsbHub.Core;

namespace UsbHub.App.Native;

internal static class NativeHid
{
    internal sealed record Reading(int? Percent, string Name, string Manufacturer, string Kind, string Detail);
    internal sealed record InterfaceCaps(ushort UsagePage, ushort Usage, ushort InputLength, ushort OutputLength);

    public static InterfaceCaps? Describe(string path)
    {
        using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var data)) return null;
        try { return HidP_GetCaps(data, out var caps) == 0x00110000 ? new(caps.UsagePage, caps.Usage, caps.InputLength, caps.OutputLength) : null; }
        finally { HidD_FreePreparsedData(data); }
    }

    public static Reading ReadBattery(string path)
    {
        using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) return new(null, "", "", "device", "HID 接口被系统或厂商程序占用，无法读取。" );
        var name = GetString(handle, false);
        var manufacturer = GetString(handle, true);
        if (!HidD_GetPreparsedData(handle, out var preparsed)) return new(null, name, manufacturer, "device", "无法读取 HID 报告描述符。" );
        try
        {
            if (HidP_GetCaps(preparsed, out var caps) != 0x00110000) return new(null, name, manufacturer, "device", "HID 能力解析失败。" );
            var kind = caps.UsagePage == 1 ? caps.Usage switch { 2 => "mouse", 6 => "keyboard", 4 or 5 => "controller", _ => "device" } :
                caps.UsagePage == 0x0B ? "headphones" : "device";
            var hasBatteryField = false;
            // Feature and control Input reads only. Never stream keyboard or mouse input.
            foreach (var type in new[] { 2, 0 })
            {
                ushort count = type == 2 ? caps.FeatureValueCount : caps.InputValueCount;
                var length = type == 2 ? caps.FeatureLength : caps.InputLength;
                if (count == 0 || count > 1024 || length < 1 || length > 8192) continue;
                var values = new ValueCaps[count];
                if (HidP_GetValueCaps(type, values, ref count, preparsed) != 0x00110000) continue;
                for (var i = 0; i < count; i++)
                {
                    var value = values[i];
                    var isStrength = value.UsagePage == 0x06 && HasUsage(value, 0x20);
                    var isRelativeCapacity = value.UsagePage == 0x85 && HasUsage(value, 0x64);
                    var isRemaining = value.UsagePage == 0x85 && HasUsage(value, 0x66);
                    if (!isStrength && !isRelativeCapacity && !isRemaining) continue;
                    hasBatteryField = true;
                    var report = new byte[length];
                    report[0] = value.ReportId;
                    var read = type == 2 ? HidD_GetFeature(handle, report, report.Length) : HidD_GetInputReport(handle, report, report.Length);
                    if (!read) continue;
                    var usage = (ushort)(isStrength ? 0x20 : isRelativeCapacity ? 0x64 : 0x66);
                    if (HidP_GetUsageValue(type, value.UsagePage, value.LinkCollection, usage, out var raw, preparsed, report, report.Length) != 0x00110000) continue;
                    int? percent = null;
                    if (isStrength) percent = BatteryDecoder.Normalize(raw, value.LogicalMin, value.LogicalMax);
                    if (isRelativeCapacity) percent = BatteryDecoder.Percentage((int)raw);
                    if (isRemaining)
                    {
                        // RemainingCapacity can be mAh, mWh or percentage. Require FullChargeCapacity.
                        for (var j = 0; j < count; j++)
                        {
                            var full = values[j];
                            if (full.UsagePage != 0x85 || full.LinkCollection != value.LinkCollection || !HasUsage(full, 0x67)) continue;
                            var fullReport = report;
                            if (full.ReportId != value.ReportId)
                            {
                                fullReport = new byte[length];
                                fullReport[0] = full.ReportId;
                                if (!(type == 2 ? HidD_GetFeature(handle, fullReport, fullReport.Length) : HidD_GetInputReport(handle, fullReport, fullReport.Length))) continue;
                            }
                            if (HidP_GetUsageValue(type, 0x85, full.LinkCollection, 0x67, out var fullRaw, preparsed, fullReport, fullReport.Length) == 0x00110000)
                                percent = BatteryDecoder.Capacity(raw, fullRaw);
                        }
                    }
                    if (percent.HasValue) return new(percent, name, manufacturer, kind,
                        $"HID {(type == 2 ? "Feature" : "Input")} · Usage {value.UsagePage:X2}/{usage:X2} · 标准报告直接读取");
                }
            }
            return new(null, name, manufacturer, kind, hasBatteryField
                ? "描述符包含电量字段，但设备未返回可用值；可能处于休眠或访问受限。"
                : "HID 描述符未公开标准电量字段；2.4 GHz 私有协议需单独适配。" );
        }
        finally { HidD_FreePreparsedData(preparsed); }
    }

    private static bool HasUsage(ValueCaps caps, ushort usage) => caps.IsRange != 0
        ? caps.UsageMin <= usage && caps.UsageMax >= usage : caps.UsageMin == usage;

    private static string GetString(SafeFileHandle handle, bool manufacturer)
    {
        var buffer = new byte[512];
        var result = manufacturer ? HidD_GetManufacturerString(handle, buffer, buffer.Length) : HidD_GetProductString(handle, buffer, buffer.Length);
        return result ? Encoding.Unicode.GetString(buffer).TrimEnd('\0') : "";
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct HidCaps
    {
        [FieldOffset(0)] public ushort Usage;
        [FieldOffset(2)] public ushort UsagePage;
        [FieldOffset(4)] public ushort InputLength;
        [FieldOffset(6)] public ushort OutputLength;
        [FieldOffset(8)] public ushort FeatureLength;
        [FieldOffset(48)] public ushort InputValueCount;
        [FieldOffset(60)] public ushort FeatureValueCount;
    }
    [StructLayout(LayoutKind.Explicit, Size = 72)] private struct ValueCaps
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportId;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(56)] public ushort UsageMin;
        [FieldOffset(58)] public ushort UsageMax;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
    [DllImport("hid.dll")] private static extern int HidP_GetValueCaps(int type, [Out] ValueCaps[] caps, ref ushort count, IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetUsageValue(int type, ushort page, ushort collection, ushort usage, out uint value, IntPtr data, byte[] report, int length);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetInputReport(SafeFileHandle handle, [In, Out] byte[] report, int length);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetProductString(SafeFileHandle handle, [Out] byte[] buffer, int length);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetManufacturerString(SafeFileHandle handle, [Out] byte[] buffer, int length);
}
