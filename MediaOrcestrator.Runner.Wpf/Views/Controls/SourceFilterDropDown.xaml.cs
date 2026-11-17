using MediaOrcestrator.Runner.Wpf.ViewModels;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

public partial class SourceFilterDropDown : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(SourceFilterDropDown),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(SourceFilterDropDown),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CanEditProperty = DependencyProperty.Register(
        nameof(CanEdit),
        typeof(bool),
        typeof(SourceFilterDropDown),
        new PropertyMetadata(true));

    public static readonly DependencyProperty SelectAllCommandProperty = DependencyProperty.Register(
        nameof(SelectAllCommand),
        typeof(ICommand),
        typeof(SourceFilterDropDown),
        new PropertyMetadata(null));

    public static readonly DependencyProperty SelectNoneCommandProperty = DependencyProperty.Register(
        nameof(SelectNoneCommand),
        typeof(ICommand),
        typeof(SourceFilterDropDown),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PopupMinWidthProperty = DependencyProperty.Register(
        nameof(PopupMinWidth),
        typeof(double),
        typeof(SourceFilterDropDown),
        new PropertyMetadata(240d));

    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
        nameof(Summary),
        typeof(string),
        typeof(SourceFilterDropDown),
        new PropertyMetadata("0"));

    private INotifyCollectionChanged? _collection;
    private readonly List<SourceCheckOptionViewModel> _hookedItems = [];
    private bool _isAttached;

    public SourceFilterDropDown()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool CanEdit
    {
        get => (bool)GetValue(CanEditProperty);
        set => SetValue(CanEditProperty, value);
    }

    public ICommand? SelectAllCommand
    {
        get => (ICommand?)GetValue(SelectAllCommandProperty);
        set => SetValue(SelectAllCommandProperty, value);
    }

    public ICommand? SelectNoneCommand
    {
        get => (ICommand?)GetValue(SelectNoneCommandProperty);
        set => SetValue(SelectNoneCommandProperty, value);
    }

    public double PopupMinWidth
    {
        get => (double)GetValue(PopupMinWidthProperty);
        set => SetValue(PopupMinWidthProperty, value);
    }

    public string Summary
    {
        get => (string)GetValue(SummaryProperty);
        private set => SetValue(SummaryProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SourceFilterDropDown)d).OnItemsSourceChanged(e.NewValue as IEnumerable);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isAttached = true;
        Attach(ItemsSource);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isAttached = false;
        Detach();
    }

    private void OnItemsSourceChanged(IEnumerable? newValue)
    {
        if (_isAttached || IsLoaded)
        {
            Attach(newValue);
        }
        else
        {
            Detach();
            UpdateSummary(newValue);
        }
    }

    private void Attach(IEnumerable? items)
    {
        Detach();

        if ((_isAttached || IsLoaded) && items is not null)
        {
            if (items is INotifyCollectionChanged incc)
            {
                _collection = incc;
                _collection.CollectionChanged += OnCollectionChanged;
            }

            HookItems(items);
        }

        UpdateSummary(items);
    }

    private void Detach()
    {
        if (_collection is not null)
        {
            _collection.CollectionChanged -= OnCollectionChanged;
            _collection = null;
        }

        UnhookItems();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnhookItems();
        HookItems(ItemsSource);
        Recompute();
    }

    private void HookItems(IEnumerable? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items.OfType<SourceCheckOptionViewModel>())
        {
            item.PropertyChanged += OnItemChanged;
            _hookedItems.Add(item);
        }
    }

    private void UnhookItems()
    {
        foreach (var item in _hookedItems)
        {
            item.PropertyChanged -= OnItemChanged;
        }

        _hookedItems.Clear();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SourceCheckOptionViewModel.IsChecked))
        {
            Recompute();
        }
    }

    private void Recompute()
    {
        UpdateSummary(_hookedItems);
    }

    private void UpdateSummary(IEnumerable? items)
    {
        if (items is null)
        {
            SetCurrentValue(SummaryProperty, "0/0");
            return;
        }

        var total = 0;
        var selected = 0;

        foreach (var item in items.OfType<SourceCheckOptionViewModel>())
        {
            total++;

            if (item.IsChecked)
            {
                selected++;
            }
        }

        SetCurrentValue(SummaryProperty, $"{selected}/{total}");
    }
}
