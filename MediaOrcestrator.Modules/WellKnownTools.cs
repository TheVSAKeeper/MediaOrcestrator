namespace MediaOrcestrator.Modules;

public static class WellKnownTools
{
    public const string FFmpeg = "ffmpeg";
    public const string YtDlp = "yt-dlp";
    public const string Deno = "deno";

    public static readonly ToolDescriptor FFmpegDescriptor = new()
    {
        Name = FFmpeg,
        GitHubRepo = "BtbN/FFmpeg-Builds",
        AssetPattern = "ffmpeg-N-*-win64-gpl.zip",
        VersionCommand = "-version",
        VersionPattern = @"ffmpeg version N-\d+-\w+-(\d{8})",
        VersionTagPattern = @"autobuild-(\d{4}-\d{2}-\d{2})",
        ArchiveType = ArchiveType.Zip,
        ArchiveExecutablePath = "ffmpeg-*/bin/ffmpeg.exe",
    };

    public static readonly ToolDescriptor FFmpegWithProbeDescriptor =
        FFmpegDescriptor with { CompanionExecutables = ["ffprobe"] };

    public static readonly ToolDescriptor YtDlpDescriptor = new()
    {
        Name = YtDlp,
        GitHubRepo = "yt-dlp/yt-dlp",
        AssetPattern = "yt-dlp.exe",
        VersionCommand = "--version",
        ArchiveType = ArchiveType.None,
    };

    public static readonly ToolDescriptor DenoDescriptor = new()
    {
        Name = Deno,
        GitHubRepo = "denoland/deno",
        AssetPattern = "deno-x86_64-pc-windows-msvc.zip",
        VersionCommand = "--version",
        VersionPattern = @"deno (\S+)",
        ArchiveType = ArchiveType.Zip,
        ArchiveExecutablePath = "deno.exe",
    };
}
