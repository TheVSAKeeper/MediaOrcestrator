using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MediaOrcestrator.Runner.Wpf.Views.Controls;

public partial class JournalPanel : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(JournalPanel),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty ClearCommandProperty = DependencyProperty.Register(
        nameof(ClearCommand),
        typeof(ICommand),
        typeof(JournalPanel),
        new PropertyMetadata(null));

    private INotifyCollectionChanged? _observedCollection;

    public JournalPanel()
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

    public ICommand? ClearCommand
    {
        get => (ICommand?)GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (JournalPanel)d;
        panel.AttachCollection(e.NewValue as INotifyCollectionChanged);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachCollection(ItemsSource as INotifyCollectionChanged);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachCollection();
    }

    private void AttachCollection(INotifyCollectionChanged? collection)
    {
        if (ReferenceEquals(_observedCollection, collection))
        {
            return;
        }

        DetachCollection();
        _observedCollection = collection;

        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged += OnEntriesCollectionChanged;
        }
    }

    private void DetachCollection()
    {
        if (_observedCollection is not null)
        {
            _observedCollection.CollectionChanged -= OnEntriesCollectionChanged;
            _observedCollection = null;
        }
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        var scrollViewer = EntriesControl.Template?.FindName("PART_ScrollViewer", EntriesControl) as ScrollViewer;

        if (scrollViewer is null)
        {
            return;
        }

        var isNearBottom = scrollViewer.VerticalOffset + scrollViewer.ViewportHeight >= scrollViewer.ExtentHeight - 16;

        if (isNearBottom)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(scrollViewer.ScrollToBottom));
        }
    }
}
