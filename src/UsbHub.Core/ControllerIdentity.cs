namespace UsbHub.Core;

public static class ControllerIdentity
{
    public static bool IsXInputHid(string instanceId) => instanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) &&
        instanceId.Contains("&IG_", StringComparison.OrdinalIgnoreCase);

    public static bool IsAuxiliary(WirelessDevice device) => IsXInputHid(device.InstanceId) &&
        device.Kind != "controller" && !device.HasBattery;

    public static ControllerStyle Style(WirelessDevice device)
    {
        if (device.ControllerStyle != ControllerStyle.Generic) return device.ControllerStyle;
        if (device.Kind != "controller") return ControllerStyle.Generic;
        if (DualSenseBatteryDecoder.Model(device.InstanceId) is not null || device.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase))
            return ControllerStyle.DualSense;
        if (device.Id.StartsWith("xinput:", StringComparison.OrdinalIgnoreCase) || IsXInputHid(device.InstanceId) ||
            device.Source.StartsWith("XInput", StringComparison.OrdinalIgnoreCase) || device.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase))
            return ControllerStyle.Xbox;
        return ControllerStyle.Generic;
    }

    public static string IllustrationKind(WirelessDevice device) => Style(device) switch
    {
        ControllerStyle.Xbox => "xbox-controller",
        ControllerStyle.DualSense => "dualsense-controller",
        _ => device.Kind
    };

    public static IReadOnlyList<WirelessDevice> WithDiscoveredControllers(IReadOnlyList<WirelessDevice> snapshot,
        IReadOnlyList<WirelessDevice> discovered)
    {
        var normalized = XInputSupport.Consolidate(snapshot);
        var unidentifiedSlots = normalized.Any(d => d.Id.StartsWith("xinput:", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(d.InstanceId));
        // Multiple XInput slots cannot be linked to individual PnP interfaces. Their cards already represent
        // those controllers; do not add each DirectInput compatibility view as another battery device.
        return DeviceMerger.Merge(normalized.Concat(discovered.Where(d => !unidentifiedSlots || !IsXInputHid(d.InstanceId))));
    }
}
