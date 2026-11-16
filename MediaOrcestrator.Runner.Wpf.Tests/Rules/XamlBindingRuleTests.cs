using KeepShell.Testing.Rules;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Rules;

[TestFixture]
public sealed class XamlBindingRuleTests
{
    private static readonly string WpfRoot = ResolveWpfRoot();

    private static IEnumerable<string> MarkupFiles => ProjectFiles.Enumerate(WpfRoot, "*.xaml");

    [TestCaseSource(nameof(MarkupFiles))]
    public void Все_pack_URI_в_разметке_содержат_имя_сборки(string file)
    {
        Assert.That(MarkupRules.PackUris(file, WpfRoot), Is.Empty);
    }

    [TestCaseSource(nameof(MarkupFiles))]
    public void Привязка_Value_у_ProgressBar_только_для_чтения(string file)
    {
        Assert.That(MarkupRules.ReadOnlyBindings(file, WpfRoot), Is.Empty);
    }

    private static string ResolveWpfRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MediaOrcestrator.sln")))
            {
                return Path.Combine(dir.FullName, "MediaOrcestrator.Runner.Wpf");
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория с MediaOrcestrator.sln.");
    }
}
