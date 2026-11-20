using KeepShell.Bootstrap;
using MediaOrcestrator.Runner.Wpf.Bootstrap;
using Serilog;
using Serilog.Events;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;

namespace MediaOrcestrator.Runner.Wpf.Tests.Smoke;

public sealed partial class LiveSmokeTests
{
    [Test]
    public void ОшибкиПривязок_ПовторыНеСкрываютНовыеОшибкиИЗначенияНеПопадаютВЖурнал()
    {
        const string first = "BindingJournalTestMissingFirst";
        const string late = "BindingJournalTestMissingLate";
        const string indexKey = "SensitiveBindingIndexKey";
        var path = Path.Combine(_fixture.Root, "binding-journal-test.log");
        var logSink = new WpfLogSink();

        using (var logger = new LoggerConfiguration().WriteTo.File(path).WriteTo.Sink(logSink).CreateLogger())
        using (new WpfBindingJournal(logger))
        {
            for (var i = 0; i < 240; i++)
            {
                CreateInvalidBinding(first);
            }
            CreateInvalidBinding(late);
            CreateInvalidBinding($"Entries[{indexKey}]");
        }

        var messages = logSink.Snapshot().Where(entry => entry.Level == LogEventLevel.Error)
            .Select(entry => entry.RenderMessage()).ToArray();
        var fileText = File.ReadAllText(path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages.Count(message => message.Contains(first, StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(messages.Count(message => message.Contains(late, StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(fileText, Does.Contain(first).And.Contain(late));
            Assert.That(fileText, Does.Contain("Entries[…]"));
            Assert.That(fileText, Does.Not.Contain(indexKey));
        }
    }

    private static void CreateInvalidBinding(string path)
    {
        var target = new TextBlock();
        target.SetBinding(TextBlock.TextProperty, new Binding(path) { Source = new object() });
    }
}
