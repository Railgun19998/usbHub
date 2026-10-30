using System;
using System.Collections.Generic;
using System.Linq;
using UsbHub.App.Native;
using UsbHub.Core;

namespace UsbHub.App.Services;

internal static class PassiveControllerDiscovery
{
    // SetupAPI metadata only: no HID handles, input reports, XInput calls or device communication.
    internal static IReadOnlyList<WirelessDevice> Discover(DateTimeOffset nextQueryAt) =>
        NativeDevices.Enumerate(NativeDevices.HidGuid).Where(n => n.IsGameController).Select(node => new WirelessDevice
        {
            Id = node.InstanceId, InstanceId = node.InstanceId, GroupId = node.ContainerId,
            Name = DualSenseBatteryDecoder.Model(node.InstanceId) ?? node.Name, Manufacturer = node.Manufacturer,
            Kind = "controller", Transport = node.IsBluetooth ? Transport.Bluetooth :
                ControllerIdentity.IsXInputHid(node.InstanceId) ? Transport.WirelessController : Transport.UsbHid,
            Source = "Windows 手柄发现", IsConnected = null,
            Detail = $"Windows 已识别此游戏设备，尚无本轮电量。点击刷新可立即读取；下次自动读取 {nextQueryAt:HH:mm:ss}。"
        }).ToArray();
}
