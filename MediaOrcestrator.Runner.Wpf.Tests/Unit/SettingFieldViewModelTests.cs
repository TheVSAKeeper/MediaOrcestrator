using KeepShell.Services.Platform;
using MediaOrcestrator.Modules;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using NSubstitute;

namespace MediaOrcestrator.Runner.Wpf.Tests.Unit;

[TestFixture]
public sealed class SettingFieldViewModelTests
{
    [TestCase("путь к папке", "Путь к папке")]
    [TestCase("apiKey", "ApiKey")]
    [TestCase("Путь к файлу", "Путь к файлу")]
    [TestCase("URL", "URL")]
    [TestCase("123 значение", "123 значение")]
    [TestCase("", "")]
    public void Title_ПоднимаетПервуюСтрочнуюБуквуБезИзмененияКлюча(string rawTitle, string expectedDisplayTitle)
    {
        var setting = new SourceSettings
        {
            Key = "raw_setting_key",
            Title = rawTitle,
        };

        var field = new SettingFieldViewModel(setting, Substitute.For<IFilePicker>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(field.Title, Is.EqualTo(expectedDisplayTitle));
            Assert.That(field.Key, Is.EqualTo("raw_setting_key"));
            Assert.That(field.Setting.Key, Is.EqualTo("raw_setting_key"));
            Assert.That(field.Setting.Title, Is.EqualTo(rawTitle));
        }
    }

    [TestCase("папка с видео", true, "Папка с видео *")]
    [TestCase("папка с видео", false, "Папка с видео")]
    public void LabelWithRequired_ФорматируетНормализованныйЗаголовок(string rawTitle, bool isRequired, string expectedLabel)
    {
        var setting = new SourceSettings
        {
            Key = "folder_path",
            Title = rawTitle,
            IsRequired = isRequired,
        };

        var field = new SettingFieldViewModel(setting, Substitute.For<IFilePicker>());

        Assert.That(field.LabelWithRequired, Is.EqualTo(expectedLabel));
    }
}
