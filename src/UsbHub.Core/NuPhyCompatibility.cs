namespace UsbHub.Core;

public static class NuPhyCompatibility
{
    // Match a model's VID/PID, never a USB port or instance-specific path suffix.
    public static WirelessDevice Annotate(WirelessDevice device)
    {
        if (!device.InstanceId.Contains("VID_19F5&PID_2620", StringComparison.OrdinalIgnoreCase)) return device;
        return device with
        {
            Name = "NuPhy Air75 V3", Kind = "keyboard", Transport = Transport.UsbReceiver,
            Source = device.HasBattery ? device.Source : "NuPhy Air75 V3 · 电量暂未支持",
            Detail = device.Detail + "\n已识别 19F5:2620 接收器。当前未确认 Air75 V3 的 2.4 GHz 电量查询协议；不发送猜测命令。" +
                "\n蓝牙模式可尝试标准 Battery Service，需连接后验证。换 USB 口不影响型号识别。"
        };
    }
}
