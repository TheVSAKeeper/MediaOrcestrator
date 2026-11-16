using KeepShell.Services.Modal;
using KeepShell.Testing.Rules;

namespace MediaOrcestrator.Runner.Wpf.Tests.Rules;

[TestFixture]
public sealed class DialogViewPairsTests
{
    [Test]
    public void Каждой_модели_диалога_соответствует_представление()
    {
        var missing = ViewPairRule.MissingViews(
            typeof(App).Assembly,
            static type => !type.IsAbstract
                && (ViewPairRule.IsDialogViewModel(type) || typeof(IDialogViewModel).IsAssignableFrom(type)));

        Assert.That(missing, Is.Empty);
    }
}
