using MediaOrcestrator.Modules;
using Microsoft.Extensions.Logging;

namespace MediaOrcestrator.HardDiskDrive;

public sealed class HardDiskDriveCodecConverter(
    ILogger<HardDiskDriveCodecConverter> logger,
    VideoTranscoder videoTranscoder)
{
    public async Task ConvertAsync(
        int typeId,
        string externalId,
        string srcFilePath,
        TimeSpan totalDuration,
        IProgress<ConvertProgress>? progress,
        CancellationToken cancellationToken)
    {
        var label = typeId == 1 ? "VP9→H264" : "H264→VP9";
        logger.ConversionStarting(label, externalId);

        var outputExt = typeId == 2 ? ".webm" : ".mp4";
        var convertPath = srcFilePath + "_convert" + outputExt;
        var backupPath = srcFilePath + ".bak";
        var conversionSucceeded = false;

        try
        {
            var fileName = Path.GetFileName(srcFilePath);
            IProgress<double>? wrappedProgress = progress == null
                ? null
                : new Progress<double>(p => progress.Report(new(p, fileName)));

            var success = typeId == 1
                ? await videoTranscoder.TranscodeVp9ToH264Async(srcFilePath, convertPath, totalDuration, wrappedProgress, cancellationToken)
                : await videoTranscoder.TranscodeH264ToVp9Async(srcFilePath, convertPath, totalDuration, wrappedProgress, cancellationToken);

            if (!success)
            {
                logger.ConversionFailed(externalId);
                return;
            }

            var convertedFileInfo = new FileInfo(convertPath);
            if (!convertedFileInfo.Exists || convertedFileInfo.Length == 0)
            {
                logger.ConvertedFileInvalid(convertPath);
                return;
            }

            File.Move(srcFilePath, backupPath, true);

            try
            {
                File.Move(convertPath, srcFilePath, true);
            }
            catch
            {
                File.Move(backupPath, srcFilePath, true);
                throw;
            }

            File.Delete(backupPath);
            conversionSucceeded = true;

            logger.ConversionCompleted(externalId);
        }
        finally
        {
            if (!conversionSucceeded && File.Exists(convertPath))
            {
                try
                {
                    File.Delete(convertPath);
                }
                catch (Exception ex)
                {
                    logger.TempFileDeleteFailed(convertPath, ex);
                }
            }

            if (File.Exists(backupPath))
            {
                if (!File.Exists(srcFilePath))
                {
                    try
                    {
                        File.Move(backupPath, srcFilePath, true);
                    }
                    catch (Exception ex)
                    {
                        logger.BackupRestoreFailed(backupPath, ex);
                    }
                }
                else
                {
                    try
                    {
                        File.Delete(backupPath);
                    }
                    catch (Exception ex)
                    {
                        logger.BackupFileDeleteFailed(backupPath, ex);
                    }
                }
            }
        }
    }
}
