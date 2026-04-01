using System.Globalization;

namespace MediaOrcestrator.Modules;

public static class SpeedLimitHelper
{
    public static long? ParseDownloadBytesPerSecond(Dictionary<string, string> settings)
    {
        return ParseMbps(settings, "speed_limit");
    }

    public static long? ParseUploadBytesPerSecond(Dictionary<string, string> settings)
    {
        return ParseMbps(settings, "upload_speed_limit");
    }

    private static long? ParseMbps(Dictionary<string, string> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!double.TryParse(value, CultureInfo.InvariantCulture, out var mbps) || mbps <= 0)
        {
            return null;
        }

        return (long)(mbps * 1_000_000 / 8);
    }
}
