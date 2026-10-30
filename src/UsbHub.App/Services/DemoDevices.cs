using System;
using System.Collections.Generic;
using UsbHub.Core;

namespace UsbHub.App.Services;

internal static class DemoDevices
{
    public static IReadOnlyList<WirelessDevice> Create() =>
    [
        Example("mouse", "无线鼠标 · 示例", "mouse", Transport.UsbReceiver, 82),
        Example("keyboard", "桌面键盘 · 示例", "keyboard", Transport.Bluetooth, 64),
        Example("headphones", "降噪耳机 · 示例", "headphones", Transport.Bluetooth, 16),
        Example("controller", "游戏手柄 · 示例", "controller", Transport.WirelessController, null) with { Band = BatteryBand.Medium, Source = "示例 · XInput 等级" },
        Example("receiver", "USB 接收器 · 示例", "device", Transport.UsbReceiver, null) with { IsConnected = null, Source = "示例 · 电量未公开" },
        Example("cached", "便携键盘 · 示例", "keyboard", Transport.Bluetooth, 45) with { IsCached = true, IsConnected = null, Source = "示例 · 系统缓存" }
    ];

    private static WirelessDevice Example(string id, string name, string kind, Transport transport, int? percent) => new()
    {
        Id = "demo:" + id, Name = name, Kind = kind, Transport = transport, BatteryPercent = percent,
        IsConnected = true, ReadAt = DateTimeOffset.Now, Source = "示例 · 标准电量", Detail = "这是界面体验数据，不是真实设备。"
    };
}
