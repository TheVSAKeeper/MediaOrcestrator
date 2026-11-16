using MahApps.Metro.IconPacks;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

public partial class FilePickerField : UserControl
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path),
        typeof(string),
        typeof(FilePickerField),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPathOrLabelChanged));

    public static readonly DependencyProperty DisplayLabelProperty = DependencyProperty.Register(
        nameof(DisplayLabel),
        typeof(string),
        typeof(FilePickerField),
        new PropertyMetadata(null, OnPathOrLabelChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(FilePickerField),
        new PropertyMetadata("Нажмите или перетащите файл"));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(PackIconLucideKind),
        typeof(FilePickerField),
        new PropertyMetadata(PackIconLucideKind.File));

    public static readonly DependencyProperty AllowedExtensionsProperty = DependencyProperty.Register(
        nameof(AllowedExtensions),
        typeof(string),
        typeof(FilePickerField),
        new PropertyMetadata(null));

    public static readonly DependencyProperty BrowseCommandProperty = DependencyProperty.Register(
        nameof(BrowseCommand),
        typeof(ICommand),
        typeof(FilePickerField),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ClearCommandProperty = DependencyProperty.Register(
        nameof(ClearCommand),
        typeof(ICommand),
        typeof(FilePickerField),
        new PropertyMetadata(null));

    public static readonly DependencyProperty BrowseToolTipProperty = DependencyProperty.Register(
        nameof(BrowseToolTip),
        typeof(string),
        typeof(FilePickerField),
        new PropertyMetadata("Выбрать другой файл"));

    public static readonly DependencyProperty ClearToolTipProperty = DependencyProperty.Register(
        nameof(ClearToolTip),
        typeof(string),
        typeof(FilePickerField),
        new PropertyMetadata("Очистить"));

    public static readonly DependencyProperty BrowseAutomationNameProperty = DependencyProperty.Register(
        nameof(BrowseAutomationName),
        typeof(string),
        typeof(FilePickerField),
        new PropertyMetadata("Выбрать файл"));

    public static readonly DependencyProperty ClearAutomationNameProperty = DependencyProperty.Register(
        nameof(ClearAutomationName),
        typeof(string),
        typeof(FilePickerField),
        new PropertyMetadata("Очистить файл"));

    public static readonly DependencyProperty IsPathEditableProperty = DependencyProperty.Register(
        nameof(IsPathEditable),
        typeof(bool),
        typeof(FilePickerField),
        new PropertyMetadata(false));

    private bool _isDragValid;

    private static readonly DependencyPropertyKey HasFilePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasFile),
        typeof(bool),
        typeof(FilePickerField),
        new PropertyMetadata(false));

    public static readonly DependencyProperty HasFileProperty = HasFilePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ResolvedLabelPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ResolvedLabel),
        typeof(string),
        typeof(FilePickerField),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ResolvedLabelProperty = ResolvedLabelPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsDragOverValidPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsDragOverValid),
        typeof(bool),
        typeof(FilePickerField),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsDragOverValidProperty = IsDragOverValidPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey DropErrorTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DropErrorText),
        typeof(string),
        typeof(FilePickerField),
        new PropertyMetadata(null));

    public static readonly DependencyProperty DropErrorTextProperty = DropErrorTextPropertyKey.DependencyProperty;

    public FilePickerField()
    {
        InitializeComponent();
    }

    public string? Path
    {
        get => (string?)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public string? DisplayLabel
    {
        get => (string?)GetValue(DisplayLabelProperty);
        set => SetValue(DisplayLabelProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public PackIconLucideKind Icon
    {
        get => (PackIconLucideKind)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? AllowedExtensions
    {
        get => (string?)GetValue(AllowedExtensionsProperty);
        set => SetValue(AllowedExtensionsProperty, value);
    }

    public ICommand? BrowseCommand
    {
        get => (ICommand?)GetValue(BrowseCommandProperty);
        set => SetValue(BrowseCommandProperty, value);
    }

    public ICommand? ClearCommand
    {
        get => (ICommand?)GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    public string BrowseToolTip
    {
        get => (string)GetValue(BrowseToolTipProperty);
        set => SetValue(BrowseToolTipProperty, value);
    }

    public string ClearToolTip
    {
        get => (string)GetValue(ClearToolTipProperty);
        set => SetValue(ClearToolTipProperty, value);
    }

    public string BrowseAutomationName
    {
        get => (string)GetValue(BrowseAutomationNameProperty);
        set => SetValue(BrowseAutomationNameProperty, value);
    }

    public string ClearAutomationName
    {
        get => (string)GetValue(ClearAutomationNameProperty);
        set => SetValue(ClearAutomationNameProperty, value);
    }

    public bool IsPathEditable
    {
        get => (bool)GetValue(IsPathEditableProperty);
        set => SetValue(IsPathEditableProperty, value);
    }

    public bool HasFile => (bool)GetValue(HasFileProperty);

    public string ResolvedLabel => (string)GetValue(ResolvedLabelProperty);

    public bool IsDragOverValid => (bool)GetValue(IsDragOverValidProperty);

    public string? DropErrorText => (string?)GetValue(DropErrorTextProperty);

    private static void OnPathOrLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (FilePickerField)d;
        var path = control.Path;
        var hasFile = !string.IsNullOrWhiteSpace(path);
        control.SetValue(HasFilePropertyKey, hasFile);

        var label = !string.IsNullOrWhiteSpace(control.DisplayLabel)
            ? control.DisplayLabel!
            : (hasFile ? System.IO.Path.GetFileName(path!) : string.Empty);

        control.SetValue(ResolvedLabelPropertyKey, label);

        if (e.Property == PathProperty)
        {
            control.SetValue(DropErrorTextPropertyKey, null);
        }
    }

    private void OnBodyMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsPathEditable && IsEnabled && BrowseCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPreviewDragEnter(object sender, DragEventArgs e)
    {
        _isDragValid = IsEnabled && TryExtractFile(e, out _, out _);
        ApplyDragEffects(e);
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e) => ApplyDragEffects(e);

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        _isDragValid = false;
        SetValue(IsDragOverValidPropertyKey, false);
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        _isDragValid = false;
        SetValue(IsDragOverValidPropertyKey, false);

        if (!IsEnabled)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!TryExtractFile(e, out var filePath, out var error))
        {
            SetValue(DropErrorTextPropertyKey, error);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        SetValue(DropErrorTextPropertyKey, null);
        SetCurrentValue(PathProperty, filePath);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ApplyDragEffects(DragEventArgs e)
    {
        if (_isDragValid)
        {
            e.Effects = DragDropEffects.Copy;
            SetValue(IsDragOverValidPropertyKey, true);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            SetValue(IsDragOverValidPropertyKey, false);
        }

        e.Handled = true;
    }

    private bool TryExtractFile(DragEventArgs e, out string? filePath, out string? error)
    {
        filePath = null;
        error = null;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] files
            || files.Length == 0)
        {
            error = "Перетащите файл с диска.";
            return false;
        }

        var candidate = files[0];

        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
        {
            error = "Нужен существующий файл, а не папка.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(AllowedExtensions))
        {
            var ext = System.IO.Path.GetExtension(candidate);
            var allowed = AllowedExtensions.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (!allowed.Any(a => string.Equals(a, ext, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"Неподдерживаемый формат ({ext}). Ожидаются: {string.Join(", ", allowed)}";
                return false;
            }
        }

        filePath = candidate;
        return true;
    }
}
