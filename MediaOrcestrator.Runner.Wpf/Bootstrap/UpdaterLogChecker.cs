using Serilog;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal static class UpdaterLogChecker
{
    public static UpdaterLogResult Check()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "updater.log");

        if (!File.Exists(logPath))
        {
            return UpdaterLogResult.Absent;
        }

        try
        {
            var content = File.ReadAllText(logPath);

            if (content.Contains("ОШИБКА"))
            {
                Log.Warning("Обнаружены ошибки в updater.log:\n{Content}", content);

                try
                {
                    File.Delete(logPath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Не удалось удалить updater.log после чтения");
                }

                return UpdaterLogResult.HasErrors;
            }

            Log.Information("Обновление прошло успешно (updater.log)");
            File.Delete(logPath);
            return UpdaterLogResult.Ok;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось прочитать updater.log");
            return UpdaterLogResult.ReadFailed;
        }
    }
}

internal enum UpdaterLogResult
{
    None = 0,
    Absent = 1,
    Ok = 2,
    HasErrors = 3,
    ReadFailed = 4,
}
