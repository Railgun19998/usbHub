namespace UsbHub.Core;

public static class VendorBatteryDecoder
{
    public static int? AsusPelta(ReadOnlySpan<byte> report)
    {
        if (report.Length < 9 || report[0] != 0xCC || report[1] != 0x12 || report[2] != 0x07 ||
            (report[5] == 0xFF && report[6] == 0xAA)) return null;
        return BatteryDecoder.Percentage(report[6]);
    }

    public static bool? AsusPeltaCharging(ReadOnlySpan<byte> report) => report.Length >= 6 &&
        report[0] == 0xCC && report[1] == 0x12 && report[2] == 0x08 && report[5] <= 1
        ? report[5] == 1 : null;

    public static bool MatchesHidpp(ReadOnlySpan<byte> report, byte slot, byte feature, byte functionAndSoftwareId) =>
        report.Length >= 7 && report[0] is 0x10 or 0x11 && report[1] == slot && report[2] == feature && report[3] == functionAndSoftwareId;
}
