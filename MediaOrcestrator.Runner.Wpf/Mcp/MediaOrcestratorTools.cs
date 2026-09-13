using KeepShell.Mcp;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MediaOrcestrator.Runner.Wpf.Mcp;

[McpServerToolType]
public sealed class MediaOrcestratorTools
{
    private MediaOrcestratorTools()
    {
    }

    [McpServerTool(Name = "get_app_state")]
    [Description("Доменное состояние MediaOrcestrator: версия сборки, путь к базе, список площадок-источников с типом и временем последнего досинка, активные задачи с прогрессом, последние десять завершённых с длительностью и ошибкой, признак идущего синка. Звать первым – по этому ответу видно, занято приложение или свободно, и есть ли смысл дальше ходить по экранам. Данные читаются в обход окна, поэтому ответ не зависит от открытой страницы.")]
    public static string GetAppState(MediaOrcestratorAutomation automation)
    {
        ArgumentNullException.ThrowIfNull(automation);

        return McpFormat.Serialize(automation.GetState());
    }
}
