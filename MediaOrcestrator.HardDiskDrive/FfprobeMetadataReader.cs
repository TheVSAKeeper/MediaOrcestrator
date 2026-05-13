using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace MediaOrcestrator.HardDiskDrive;

public sealed class FfprobeMetadataReader(
    IToolPathProvider toolPathProvider,
    ILogger<FfprobeMetadataReader> logger)
{
    public async Task<List<MetadataItem>?> ReadAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var ffprobePath = toolPathProvider.GetCompanionPath(WellKnownTools.FFmpeg, "ffprobe");

        if (ffprobePath is null)
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);

            if (process is null)
            {
                return null;
            }

            await using (cancellationToken.Register(ForceStop))
            {
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode == 0)
                {
                    return ParseOutput(output);
                }

                logger.FfprobeNonZeroExit(process.ExitCode, filePath);
                return null;
            }

            void ForceStop()
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.FfprobeFailed(filePath, ex);
            return null;
        }
    }

    private static List<MetadataItem>? ParseOutput(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = new List<MetadataItem>();

        if (root.TryGetProperty("format", out var format))
        {
            if (format.TryGetProperty("duration", out var duration)
                && double.TryParse(duration.GetString(), CultureInfo.InvariantCulture, out var seconds))
            {
                result.Add(new()
                {
                    Key = "Duration",
                    DisplayName = "Длительность",
                    Value = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss"),
                    DisplayType = "System.TimeSpan",
                });
            }

            if (format.TryGetProperty("bit_rate", out var bitRate))
            {
                result.Add(new()
                {
                    Key = "Bitrate",
                    DisplayName = "Битрейт",
                    Value = bitRate.GetString() ?? "",
                    DisplayType = "Bitrate",
                });
            }
        }

        if (!root.TryGetProperty("streams", out var streams))
        {
            return result.Count > 0 ? result : null;
        }

        foreach (var stream in streams.EnumerateArray())
        {
            var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;

            if (codecType != "video")
            {
                continue;
            }

            if (stream.TryGetProperty("width", out var w) && stream.TryGetProperty("height", out var h))
            {
                result.Add(new()
                {
                    Key = "Resolution",
                    DisplayName = "Разрешение",
                    Value = $"{w.GetInt32()}x{h.GetInt32()}",
                });
            }

            if (stream.TryGetProperty("codec_name", out var videoCodec))
            {
                result.Add(new()
                {
                    Key = "VideoCodec",
                    DisplayName = "Видеокодек",
                    Value = videoCodec.GetString() ?? "",
                });
            }

            if (stream.TryGetProperty("r_frame_rate", out var frameRate))
            {
                var fpsStr = frameRate.GetString();
                if (fpsStr is not null && TryParseFraction(fpsStr, out var fps))
                {
                    result.Add(new()
                    {
                        Key = "FrameRate",
                        DisplayName = "Частота кадров",
                        Value = fps.ToString("F2", CultureInfo.InvariantCulture),
                        DisplayType = "FPS",
                    });
                }
            }

            break;
        }

        foreach (var stream in streams.EnumerateArray())
        {
            var codecType = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;

            if (codecType == "audio")
            {
                if (stream.TryGetProperty("codec_name", out var audioCodec))
                {
                    result.Add(new()
                    {
                        Key = "AudioCodec",
                        DisplayName = "Аудиокодек",
                        Value = audioCodec.GetString() ?? "",
                    });
                }

                if (stream.TryGetProperty("sample_rate", out var sampleRate))
                {
                    result.Add(new()
                    {
                        Key = "SampleRate",
                        DisplayName = "Частота дискретизации",
                        Value = sampleRate.GetString() ?? "",
                        DisplayType = "Hz",
                    });
                }

                break;
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static bool TryParseFraction(
        string value,
        out double result)
    {
        result = 0;
        var parts = value.Split('/');

        if (parts.Length == 2
            && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var denominator)
            && denominator != 0)
        {
            result = numerator / denominator;
            return true;
        }

        return double.TryParse(value, CultureInfo.InvariantCulture, out result);
    }
}
