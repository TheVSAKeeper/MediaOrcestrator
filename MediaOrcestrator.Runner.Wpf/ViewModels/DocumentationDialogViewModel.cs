using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using Serilog;
using System.Collections.ObjectModel;
using System.IO;

namespace MediaOrcestrator.Runner.Wpf.ViewModels;

public sealed partial class DocumentationDialogViewModel : ObservableObject, IDialogViewModel
{
    private readonly IShellLauncher _shellLauncher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleDocs))]
    private DocFileEntry? _selected;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string? _openError;

    public DocumentationDialogViewModel(
        string title,
        IEnumerable<string> docFiles,
        IShellLauncher shellLauncher,
        string? initialFile = null)
    {
        _shellLauncher = shellLauncher;
        Title = title;
        Docs = new(docFiles.Select(path => new DocFileEntry(path)));

        var initial = initialFile is not null
            ? Docs.FirstOrDefault(d => string.Equals(d.Path, initialFile, StringComparison.OrdinalIgnoreCase))
            : null;

        Selected = initial ?? Docs.FirstOrDefault();
    }

    public event EventHandler<bool>? RequestClose;

    public string Title { get; }

    public ObservableCollection<DocFileEntry> Docs { get; }

    public bool HasMultipleDocs => Docs.Count > 1;

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this, false);
    }

    [RelayCommand]
    private void OpenInDefaultApp()
    {
        if (Selected is not { } doc)
        {
            return;
        }

        OpenError = null;
        if (!File.Exists(doc.Path))
        {
            Log.Warning("Файл руководства {Document} больше недоступен", doc.Name);
            OpenError = "Файл руководства больше недоступен.";
        }
        else if (!_shellLauncher.Open(doc.Path))
        {
            OpenError = "Не удалось открыть руководство во внешнем редакторе.";
        }
    }

    public void OpenExternalLink(Uri uri)
    {
        OpenError = _shellLauncher.Open(uri.ToString()) ? null : "Не удалось открыть ссылку.";
    }

    partial void OnSelectedChanged(DocFileEntry? value)
    {
        OpenError = null;
        if (value is null)
        {
            Content = string.Empty;
            return;
        }

        try
        {
            Content = File.ReadAllText(value.Path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось прочитать файл руководства {Document}", value.Name);
            Content = "Не удалось прочитать файл руководства. Подробности в журнале.";
        }
    }
}

public sealed class DocFileEntry
{
    public DocFileEntry(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
    }

    public string Path { get; }

    public string Name { get; }

    public override string ToString()
    {
        return Name;
    }
}
