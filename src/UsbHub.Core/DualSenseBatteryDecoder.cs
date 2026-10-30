using System.Buffers.Binary;

namespace UsbHub.Core;

public sealed record DualSenseBatteryReading(BatteryRange? Range, bool? IsCharging, string Detail);

public static class DualSenseBatteryDecoder
{
    public static string? Model(string instanceId)
    {
        if (instanceId.Contains("VID_054C&PID_0CE6", StringComparison.OrdinalIgnoreCase)) return "DualSense (PS5)";
        if (instanceId.Contains("VID_054C&PID_0DF2", StringComparison.OrdinalIgnoreCase)) return "DualSense Edge (PS5)";
        return null;
    }

    public static DualSenseBatteryReading? Decode(ReadOnlySpan<byte> report, bool bluetooth)
    {
        var offset = bluetooth ? 54 : 53;
        if (report.Length != (bluetooth ? 78 : 64) || report[0] != (bluetooth ? 0x31 : 0x01)) return null;
        if (bluetooth)
        {
            var crc = UpdateCrc(0xFFFFFFFF, 0xA1);
            foreach (var value in report[..^4]) crc = UpdateCrc(crc, value);
            if (~crc != BinaryPrimitives.ReadUInt32LittleEndian(report[^4..])) return null;
        }
        var status = report[offset];
        var level = status & 0x0F;
        var charging = status >> 4;
        if (charging is not (0 or 1 or 2))
            return new(null, null, "手柄报告充电异常或未知状态，未将错误状态解释为低电量。");
        if (level > 10) return new(null, charging == 1, "手柄返回无效电量档位，电量未知。");
        var minimum = charging == 2 ? 100 : level * 10;
        var maximum = minimum == 100 ? 100 : minimum + 9;
        return new(new(minimum, maximum), charging == 1,
            "设备以 10% 分档报告电量，显示真实区间；电量条取区间下界，提醒仅在区间上界不超过阈值时触发。");
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        return crc;
    }
}
