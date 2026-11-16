using KeepShell.Testing.Rules;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.Tests.Rules;

[TestFixture]
public sealed class ArchitectureRuleTests
{
    private const int MaxFileLines = 500;

    private static readonly string RepoRoot = ResolveRepoRoot();
    private static readonly string WpfRoot = Path.Combine(RepoRoot, "MediaOrcestrator.Runner.Wpf");
    private static readonly string WpfTestsRoot = Path.Combine(RepoRoot, "MediaOrcestrator.Runner.Wpf.Tests");

    private static IEnumerable<string> SourceFiles =>
        ProjectFiles.Enumerate(WpfRoot, "*.cs")
            .Concat(ProjectFiles.Enumerate(WpfRoot, "*.xaml"))
            .Concat(ProjectFiles.Enumerate(WpfTestsRoot, "*.cs"))
            .Concat(ProjectFiles.Enumerate(WpfTestsRoot, "*.xaml"));

    [TestCaseSource(nameof(SourceFiles))]
    public void Все_файлы_кода_и_разметки_держатся_не_длиннее_500_строк(string file)
    {
        var lineCount = File.ReadAllLines(file).Length;
        Assert.That(lineCount, Is.LessThanOrEqualTo(MaxFileLines), $"{Path.GetRelativePath(RepoRoot, file)}: {lineCount} строк");
    }

    [Test]
    public void Модели_представления_не_обращаются_к_платформе_мимо_шлюзов()
    {
        var viewModelFiles = ProjectFiles.Enumerate(Path.Combine(WpfRoot, "ViewModels"), "*.cs");
        var hits = PlatformCallRule.Check(viewModelFiles, RepoRoot);

        Assert.That(hits, Is.Empty);
    }

    [Test]
    public void Фронтенд_не_ссылается_на_WinForms()
    {
        var csprojPath = Path.Combine(WpfRoot, "MediaOrcestrator.Runner.Wpf.csproj");
        var propsPath = Path.Combine(RepoRoot, "Directory.Build.props");

        var csproj = File.ReadAllText(csprojPath);
        var props = File.ReadAllText(propsPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(csproj, Does.Not.Contain("MediaOrcestrator.Runner.csproj").IgnoreCase);
            Assert.That(csproj, Does.Not.Contain("UseWindowsForms").IgnoreCase);
            Assert.That(props, Does.Not.Contain("MediaOrcestrator.Runner.csproj").IgnoreCase);
            Assert.That(props, Does.Not.Contain("UseWindowsForms").IgnoreCase);
        }
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MediaOrcestrator.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория с MediaOrcestrator.sln.");
    }
}
