namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

public static class BackdropSettings
{
    public const string Key = "_system_backdrop";

    public static BackdropKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return BackdropKind.Mica;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "off" or "false" or "0" or "no" or "none" => BackdropKind.None,
            "acrylic" => BackdropKind.Acrylic,
            "micaalt" or "mica_alt" or "mica-alt" or "tabbed" => BackdropKind.MicaAlt,
            _ => BackdropKind.Mica,
        };
    }

    public static string ToToken(BackdropKind kind) => kind switch
    {
        BackdropKind.None => "none",
        BackdropKind.Acrylic => "acrylic",
        BackdropKind.MicaAlt => "micaalt",
        _ => "mica",
    };
}
