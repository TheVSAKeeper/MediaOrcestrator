using KeepShell.Testing;
using MediaOrcestrator.Domain;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using MediaOrcestrator.Runner.Wpf.Views;
using MediaOrcestrator.Runner.Wpf.Views.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MediaOrcestrator.Runner.Wpf.Tests.Smoke;

public sealed partial class LiveSmokeTests
{
    [TestCase("merge")]
    [TestCase("conflicts")]
    public void Операции_СчётчикиПолностьюВидныВОднуСтроку(string page)
    {
        NavigateToPage(page);
        Settle();

        var footer = Descendants<OperationActionBar>(_window).Single(item => item.IsVisible);
        var summary = footer.LeftContent as TextBlock;
        Assert.That(summary, Is.Not.Null);
        var text = new FormattedText(
            summary.Text,
            CultureInfo.CurrentUICulture,
            summary.FlowDirection,
            new Typeface(summary.FontFamily, summary.FontStyle, summary.FontWeight, summary.FontStretch),
            summary.FontSize,
            summary.Foreground,
            VisualTreeHelper.GetDpi(summary).PixelsPerDip);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.ActualWidth, Is.GreaterThanOrEqualTo(text.Width - 1));
            Assert.That(summary.ActualHeight, Is.LessThanOrEqualTo(text.Height + 2));
        }
    }

    [TestCase(true, false, "Публикация в источник…")]
    [TestCase(false, true, "Ошибка: квота загрузки исчерпана.")]
    public void Публикация_ПриВозвратеСохраняетСтатусИПоказываетЕго(bool busy, bool error, string status)
    {
        NavigateToPage("media");
        Settle();

        var vm = _services.GetRequiredService<PublishViewModel>();
        var originalBusy = vm.IsPublishing;
        var originalError = vm.IsStatusError;
        var originalStatus = vm.StatusMessage;

        try
        {
            vm.IsPublishing = busy;
            vm.IsStatusError = error;
            vm.StatusMessage = status;

            NavigateToPage("publish");
            Settle();

            var view = Descendants<PublishView>(_window).Single(item => item.IsVisible);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(vm.IsPublishing, Is.EqualTo(busy));
                Assert.That(vm.IsStatusError, Is.EqualTo(error));
                Assert.That(Descendants<TextBlock>(view).Any(block => block.IsVisible && block.Text == status), Is.True);
            }
        }
        finally
        {
            vm.IsPublishing = originalBusy;
            vm.IsStatusError = originalError;
            vm.StatusMessage = originalStatus;
            Settle();
        }
    }

    [Test]
    public void Обслуживание_ПослеОшибкиЗагрузки_ПоказываетБэкапИОбновляетСписокПриВозврате()
    {
        NavigateToPage("tasks");
        Settle();

        var vm = _services.GetRequiredService<CleanupAndBackupsViewModel>();
        var backups = _services.GetRequiredService<DatabaseBackupService>();
        var baseline = backups.GetBackups().Select(entry => entry.FileName).ToArray();
        var logFile = Path.Combine(vm.Database.BackupsPath, "backup-log.json");
        Directory.CreateDirectory(vm.Database.BackupsPath);
        var originalLog = File.Exists(logFile) ? File.ReadAllText(logFile) : null;
        string? createdBackup = null;

        try
        {
            vm.HasLoaded = false;
            File.WriteAllText(logFile, "{invalid");
            NavigateToPage("cleanup");
            Settle();
            VisualTestHost.Run(() => vm.LoadCommand.ExecutionTask!);
            Settle();

            var view = Descendants<CleanupAndBackupsView>(_window).Single(item => item.IsVisible);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(vm.HasLoaded, Is.False);
                Assert.That(vm.LoadError, Is.Not.Empty);
                Assert.That(Descendants<TextBlock>(view).Any(block =>
                    block.IsVisible && block.Text == "Не удалось открыть очистку и бэкапы"), Is.True);
            }

            File.WriteAllText(logFile, originalLog ?? "[]");
            backups.Backup(BackupTrigger.Manual);
            createdBackup = backups.GetBackups().Single(entry => !baseline.Contains(entry.FileName)).FileName;
            VisualTestHost.Run(() => vm.LoadCommand.ExecuteAsync(null));
            Settle();

            Assert.That(vm.HasLoaded, Is.True);
            Assert.That(Descendants<TextBlock>(view).Any(block =>
                block.IsVisible && block.Text == createdBackup), Is.True);

            NavigateToPage("tasks");
            Settle();
            Assert.That(backups.DeleteBackup(createdBackup), Is.True);
            createdBackup = null;
            var lastLoad = vm.LoadCommand.ExecutionTask;
            NavigateToPage("cleanup");
            Settle();

            Assert.That(vm.LoadCommand.ExecutionTask, Is.Not.SameAs(lastLoad));
            VisualTestHost.Run(() => vm.LoadCommand.ExecutionTask!);
            Assert.That(vm.Database.BackupsCount, Is.EqualTo(baseline.Length));
        }
        finally
        {
            if (createdBackup is not null)
            {
                backups.DeleteBackup(createdBackup);
            }

            if (originalLog is null)
            {
                File.Delete(logFile);
            }
            else
            {
                File.WriteAllText(logFile, originalLog);
            }

            VisualTestHost.Run(vm.RefreshAsync);
            Settle();
        }
    }
}
