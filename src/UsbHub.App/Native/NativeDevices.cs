using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace UsbHub.App.Native;

internal static class NativeDevices
{
    internal static readonly Guid HidGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");
    private static readonly PropertyKey ContainerKey = new(new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
    // Optional Windows Bluetooth driver property. Not a universal HID requirement.
    private static readonly PropertyKey BatteryKey = new(new("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2);

    internal sealed record Node(string InstanceId, string Name, string Class, string Manufacturer, string ContainerId,
        int? CachedBattery, uint DevInst, string[] Ancestors, string? Path = null)
    {
        public bool IsBluetooth => Ancestors.Prepend(InstanceId).Any(id =>
            id.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase) || id.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<Node> Enumerate(Guid? interfaceGuid = null)
    {
        var guid = interfaceGuid ?? Guid.Empty;
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, interfaceGuid.HasValue ? 0x12u : 0x06u);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = new List<Node>();
        try
        {
            for (uint i = 0; ; i++)
            {
                var info = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
                string? path = null;
                if (interfaceGuid.HasValue)
                {
                    var iface = new InterfaceInfo { Size = Marshal.SizeOf<InterfaceInfo>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface))
                    {
                        if (Marshal.GetLastWin32Error() == 259) break;
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out var needed, ref info);
                    if (needed is < 8 or > 65536) continue;
                    var detail = Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, needed, out _, ref info)) continue;
                        path = Marshal.PtrToStringUni(detail + 4);
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
                else if (!SetupDiEnumDeviceInfo(set, i, ref info))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var id = new StringBuilder(1024);
                if (!SetupDiGetDeviceInstanceId(set, ref info, id, id.Capacity, out _)) continue;
                var container = GetProperty(set, ref info, ContainerKey, out _);
                var battery = GetProperty(set, ref info, BatteryKey, out var batteryType);
                int? percent = batteryType == 3 && battery.Length >= 1 ? Core.BatteryDecoder.Percentage(battery[0]) :
                    batteryType == 7 && battery.Length >= 4 ? Core.BatteryDecoder.Percentage(BitConverter.ToInt32(battery)) : null;
                var friendly = RegistryProperty(set, ref info, 12);
                result.Add(new Node(id.ToString(), string.IsNullOrEmpty(friendly) ? RegistryProperty(set, ref info, 0) : friendly,
                    RegistryProperty(set, ref info, 7), RegistryProperty(set, ref info, 11),
                    container.Length == 16 && new Guid(container) != Guid.Empty && new Guid(container).ToString() != "00000000-0000-0000-ffff-ffffffffffff" ? new Guid(container).ToString() : "",
                    percent, info.DevInst, Ancestors(info.DevInst), path));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    private static byte[] GetProperty(IntPtr set, ref DeviceInfo info, PropertyKey key, out uint type)
    {
        var data = new byte[256];
        return SetupDiGetDeviceProperty(set, ref info, ref key, out type, data, (uint)data.Length, out var length, 0)
            ? data[..(int)length] : Array.Empty<byte>();
    }

    private static string RegistryProperty(IntPtr set, ref DeviceInfo info, uint key)
    {
        var bytes = new byte[4096];
        return SetupDiGetDeviceRegistryProperty(set, ref info, key, out _, bytes, (uint)bytes.Length, out var size)
            ? Encoding.Unicode.GetString(bytes, 0, (int)Math.Min(size, bytes.Length)).TrimEnd('\0') : "";
    }

    private static string[] Ancestors(uint devInst)
    {
        var list = new List<string>();
        for (var depth = 0; depth < 12 && CM_Get_Parent(out var parent, devInst, 0) == 0; depth++)
        {
            var id = new StringBuilder(1024);
            if (CM_Get_Device_ID(parent, id, id.Capacity, 0) != 0) break;
            list.Add(id.ToString());
            devInst = parent;
        }
        return list.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public int Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceInfo { public int Size; public Guid ClassGuid; public uint Flags; public UIntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo data);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceInfo data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceInfo iface, IntPtr detail, uint size, out uint required, ref DeviceInfo data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DeviceInfo data, StringBuilder id, int size, out int required);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DeviceInfo data, ref PropertyKey key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DeviceInfo data, uint key, out uint type, byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_ID(uint devInst, StringBuilder id, int length, uint flags);
}
