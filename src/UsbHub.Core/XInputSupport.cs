namespace UsbHub.Core;

public static class XInputSupport
{
    public static WirelessDevice? Create(uint index, uint capabilitiesResult, uint batteryResult,
        byte batteryType, byte batteryLevel, DateTimeOffset readAt)
    {
        if (batteryResult == 0 && batteryType == 0) return null;
        if (capabilitiesResult != 0 && batteryResult != 0) return null;
        var wired = batteryResult == 0 && batteryType == 1;
        var knownBattery = batteryResult == 0 && batteryType is 2 or 3;
        var band = knownBattery && batteryLevel <= 3 ? (BatteryBand?)batteryLevel : null;
        return new WirelessDevice
        {
            Id = $"xinput:{index}", Name = $"XInput 游戏手柄 {index + 1}", Kind = "controller",
            Transport = Transport.WirelessController, IsConnected = true,
            IsBatteryPresent = wired ? false : knownBattery ? true : null, Band = band,
            ReadAt = band.HasValue ? readAt : null,
            Source = wired ? "XInput 有线供电" : band.HasValue ? "XInput 电量等级" : "XInput 设备发现",
            Detail = $"XInput 槽位 {index + 1}。" + (wired
                ? "驱动报告有线供电，不提供电池电量。"
                : batteryResult != 0 ? $"设备已连接，电量查询失败（错误码 {batteryResult}）。"
                : !knownBattery ? $"驱动未提供已知电池类型（0x{batteryType:X2}），电量未知。"
                : !band.HasValue ? $"驱动返回无效电量等级（{batteryLevel}），电量未知。"
                : "驱动提供空、低、中、满四档电量，不提供精确百分比。")
        };
    }

    public static IReadOnlyList<WirelessDevice> Consolidate(IReadOnlyList<WirelessDevice> devices)
    {
        var slots = devices.Where(d => d.Id.StartsWith("xinput:", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (slots.Length == 0) return devices;
        // IG_ identifies the DirectInput HID view exported by an XInput driver.
        var aliases = devices.Where(d => !d.Id.StartsWith("xinput:", StringComparison.OrdinalIgnoreCase) &&
            d.InstanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) &&
            d.InstanceId.Contains("&IG_", StringComparison.OrdinalIgnoreCase)).ToArray();
        var controllers = aliases.Where(d => d.Kind == "controller").ToArray();
        WirelessDevice? named = null;
        if (slots.Length == 1 && controllers.Length == 1)
        {
            var controller = controllers[0];
            // Only a single unambiguous slot/device pair can be linked by elimination.
            // Never pair multiple controllers by list order, name, or VID/PID.
            named = slots[0] with
            {
                Name = controller.Name, InstanceId = controller.InstanceId, GroupId = controller.GroupId,
                Manufacturer = controller.Manufacturer,
                Transport = controller.Transport == Transport.Bluetooth ? Transport.Bluetooth : slots[0].Transport,
                Detail = slots[0].Detail + "\n已关联本轮唯一的 XInput 手柄 HID 接口。"
            };
            if (controller.HasBattery && (!slots[0].HasBattery ||
                (!controller.IsCached && (slots[0].IsCached || controller.BatteryPercent.HasValue))))
                named = controller with { Kind = "controller" };
        }
        return devices.Where(d => !aliases.Contains(d) ||
                (d.HasBattery && (named is null || d != controllers[0])))
            .Select(d => named is not null && d == slots[0] ? named : d)
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
