using KeepShell.Bootstrap;
using KeepShell.Testing;
using MediaOrcestrator.Runner.Wpf.Bootstrap;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class FirstRunImportDetectorTests
{
    [Test]
    public void ShouldOpenOnStartup_СвежаяПустаяУстановка_ВозвращаетTrue()
    {
        var settings = new MemorySettings();

        var shouldOpen = FirstRunImportDetector.ShouldOpenOnStartup(
            isPrimaryInstance: true,
            isGallery: false,
            isApplyImport: false,
            hadLegacySettingsTxt: false,
            settings,
            sourcesCount: 0,
            mediasCount: 0);

        Assert.That(shouldOpen, Is.True);
    }

    [TestCase(false, false, false, false, false, false, 0, 0, 0, false)]
    [TestCase(true, true, false, false, false, false, 0, 0, 0, false)]
    [TestCase(true, false, true, false, false, false, 0, 0, 0, false)]
    [TestCase(true, false, false, true, false, false, 0, 0, 0, false)]
    [TestCase(true, false, false, false, true, false, 0, 0, 0, false)]
    [TestCase(true, false, false, false, false, true, 0, 0, 0, false)]
    [TestCase(true, false, false, false, false, false, 1, 0, 0, false)]
    [TestCase(true, false, false, false, false, false, 0, 1, 0, false)]
    [TestCase(true, false, false, false, false, false, 0, 0, 1, false)]
    [TestCase(true, false, false, false, false, false, 0, 0, 0, true)]
    public void ShouldOpenOnStartup_ПриЛюбомБлокирующемУсловии_ВозвращаетFalse(
        bool isPrimary,
        bool isGallery,
        bool isApplyImport,
        bool hadLegacySettingsTxt,
        bool dismissed,
        bool completed,
        int sourcesCount,
        int mediasCount,
        int relationsCount,
        bool hasCustomPaths)
    {
        ISettingsStore settings = new MemorySettings();
        if (dismissed)
        {
            settings.SetBool(FirstRunImportDetector.PromptDismissedKey, true);
        }

        if (completed)
        {
            settings.SetBool(FirstRunImportDetector.CompletedKey, true);
        }

        var shouldOpen = FirstRunImportDetector.ShouldOpenOnStartup(
            isPrimary,
            isGallery,
            isApplyImport,
            hadLegacySettingsTxt,
            settings,
            sourcesCount,
            mediasCount,
            relationsCount,
            hasCustomPaths);

        Assert.That(shouldOpen, Is.False);
    }

    [TestCase("--apply-import=1234", 1234)]
    [TestCase("--apply-import 5678", 5678)]
    [TestCase("--nav=media", null)]
    [TestCase("--apply-import=abc", null)]
    [TestCase("--apply-import=0", null)]
    public void TryGetApplyImportPid_РазбираетАргументыКоманднойСтроки(string commandLine, int? expectedPid)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var actual = StartupCommandLine.TryGetApplyImportPid(args);

        Assert.That(actual, Is.EqualTo(expectedPid));
    }
}
