using Serilog;
using System.Reflection;
using System.Windows;
using System.Windows.Shell;

namespace MediaOrcestrator.Runner.Wpf.Bootstrap;

internal static class JumpListBuilder
{
    private const string Category = "Перейти к";

    public static void Apply()
    {
        try
        {
            var exePath = GetExecutablePath();

            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }

            var jumpList = new JumpList
            {
                ShowRecentCategory = false,
                ShowFrequentCategory = false,
            };

            jumpList.JumpItems.Add(MakeTask("Медиа", "--nav=Медиа", exePath));
            jumpList.JumpItems.Add(MakeTask("Источники и связи", "--nav=Источники и связи", exePath));
            jumpList.JumpItems.Add(MakeTask("Передачи", "--nav=Передачи", exePath));
            jumpList.JumpItems.Add(MakeTask("Лента задач", "--drawer=tasks", exePath));

            JumpList.SetJumpList(Application.Current, jumpList);
            jumpList.Apply();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось установить JumpList");
        }
    }

    private static JumpTask MakeTask(string title, string arguments, string exePath) => new()
    {
        Title = title,
        CustomCategory = Category,
        ApplicationPath = exePath,
        Arguments = arguments,
        IconResourcePath = exePath,
        IconResourceIndex = 0,
    };

    private static string GetExecutablePath()
    {
        var processPath = Environment.ProcessPath;

        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }

        return Assembly.GetEntryAssembly()?.Location ?? string.Empty;
    }
}
