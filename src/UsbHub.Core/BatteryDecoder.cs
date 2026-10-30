namespace UsbHub.Core;

public static class BatteryDecoder
{
    public static int? Percentage(int raw) => raw is >= 0 and <= 100 ? raw : null;

    public static int? Normalize(uint raw, int min, int max)
    {
        // No clamping: null / out-of-range values must never become fake 0% or 100%.
        if (min < 0 || max <= min || raw < min || raw > max) return null;
        return (int)Math.Round((raw - (double)min) * 100 / (max - (double)min));
    }

    public static int? Capacity(uint remaining, uint full) => full > 0 && remaining <= full
        ? (int)Math.Round(remaining * 100d / full) : null;
}
