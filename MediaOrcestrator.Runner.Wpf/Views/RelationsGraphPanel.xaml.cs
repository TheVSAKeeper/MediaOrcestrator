using MediaOrcestrator.Runner.Wpf.Bootstrap;
using MediaOrcestrator.Runner.Wpf.ViewModels;
using Microsoft.Msagl.WpfGraphControl;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DColor = Microsoft.Msagl.Drawing.Color;
using DGraph = Microsoft.Msagl.Drawing.Graph;
using DShape = Microsoft.Msagl.Drawing.Shape;
using DStyle = Microsoft.Msagl.Drawing.Style;

namespace MediaOrcestrator.Runner.Wpf.Views;

public partial class RelationsGraphPanel : UserControl
{
    private GraphViewer? _viewer;
    private SourcesRelationsViewModel? _vm;

    public RelationsGraphPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewer is null)
        {
            _viewer = new();
            _viewer.BindToPanel(GraphHost);
        }

        ThemeManager.Changed += OnThemeChanged;
        RebuildGraph();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.Changed -= OnThemeChanged;
        DetachFromVm();
    }

    private void OnThemeChanged(object? sender, ThemeDefinition theme)
    {
        RebuildGraph();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachFromVm();

        if (e.NewValue is SourcesRelationsViewModel vm)
        {
            _vm = vm;
            _vm.Sources.CollectionChanged += OnDataChanged;
            _vm.Relations.CollectionChanged += OnDataChanged;
            RebuildGraph();
        }
    }

    private void DetachFromVm()
    {
        if (_vm is null)
        {
            return;
        }

        _vm.Sources.CollectionChanged -= OnDataChanged;
        _vm.Relations.CollectionChanged -= OnDataChanged;
        _vm = null;
    }

    private void OnDataChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildGraph();
    }

    private void RebuildGraph()
    {
        if (_viewer is null || _vm is null)
        {
            return;
        }

        var palette = ResolvePalette();
        ApplyCanvasBackground(palette);

        var hasData = _vm.Sources.Count > 0;
        EmptyOverlay.SetCurrentValue(VisibilityProperty, hasData ? Visibility.Collapsed : Visibility.Visible);

        if (!hasData)
        {
            _viewer.Graph = null;
            return;
        }

        var graph = new DGraph("relations")
        {
            Attr =
            {
                BackgroundColor = palette.PanelBackground,
            },
        };
        var hasOut = new HashSet<string>(StringComparer.Ordinal);
        var hasIn = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relCard in _vm.Relations)
        {
            var fromId = relCard.Relation.FromId;
            var toId = relCard.Relation.ToId;

            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
            {
                continue;
            }

            hasOut.Add(fromId);
            hasIn.Add(toId);
        }

        foreach (var card in _vm.Sources)
        {
            var node = graph.AddNode(card.Source.Id);
            node.LabelText = card.Source.TitleFull;
            node.Attr.Shape = DShape.Box;

            if (card.IsDisabled)
            {
                node.Attr.FillColor = palette.DisabledFill;
                node.Attr.Color = palette.DisabledOutline;
                node.Label.FontColor = palette.DisabledLabel;
            }
            else
            {
                var role = ResolveRole(card.Source.Id, hasOut, hasIn);
                var (fill, outline) = palette.RoleColors(role);
                node.Attr.FillColor = fill;
                node.Attr.Color = outline;
                node.Label.FontColor = palette.NodeLabel;
            }
        }

        foreach (var relCard in _vm.Relations)
        {
            var fromId = relCard.Relation.FromId;
            var toId = relCard.Relation.ToId;

            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
            {
                continue;
            }

            var edge = graph.AddEdge(fromId, toId);

            if (relCard.IsDisabled)
            {
                edge.Attr.AddStyle(DStyle.Dashed);
                edge.Attr.Color = palette.EdgeDisabled;
            }
            else
            {
                edge.Attr.Color = palette.Edge;
            }
        }

        _viewer.Graph = graph;
    }

    private void ApplyCanvasBackground(ThemePalette palette)
    {
        if (_viewer?.GraphCanvas is { } canvas)
        {
            canvas.Background = new SolidColorBrush(ToMedia(palette.PanelBackground));
        }
    }

    private static NodeRole ResolveRole(string id, HashSet<string> hasOut, HashSet<string> hasIn)
    {
        var isOut = hasOut.Contains(id);
        var isIn = hasIn.Contains(id);

        return (isOut, isIn) switch
        {
            (true, false) => NodeRole.Reader,
            (false, true) => NodeRole.Writer,
            (true, true) => NodeRole.Transit,
            _ => NodeRole.Isolated,
        };
    }

    private ThemePalette ResolvePalette()
    {
        return new(
            PanelBackground: Resolve("Bg.Surface", new DColor(255, 255, 255)),
            NodeLabel: Resolve("Fg.Primary", new DColor(20, 20, 19)),
            Edge: Resolve("Fg.Secondary", new DColor(74, 71, 66)),
            EdgeDisabled: Resolve("Border.Default", new DColor(217, 214, 199)),
            DisabledFill: Resolve("Bg.SurfaceMute", new DColor(237, 235, 227)),
            DisabledOutline: Resolve("Border.Default", new DColor(217, 214, 199)),
            DisabledLabel: Resolve("Fg.Muted", new DColor(111, 107, 99)),
            IsolatedFill: Resolve("Bg.SurfaceAlt", new DColor(244, 243, 238)),
            IsolatedOutline: Resolve("Border.Default", new DColor(217, 214, 199)),
            ReaderFill: Resolve("State.SuccessSoft", new DColor(217, 236, 223)),
            ReaderOutline: Resolve("State.Success", new DColor(77, 154, 106)),
            WriterFill: Resolve("State.InfoSoft", new DColor(216, 227, 242)),
            WriterOutline: Resolve("State.Info", new DColor(74, 124, 184)),
            TransitFill: Resolve("State.WarningSoft", new DColor(242, 226, 194)),
            TransitOutline: Resolve("State.Warning", new DColor(197, 138, 42)));
    }

    private DColor Resolve(string key, DColor fallback)
    {
        if (TryFindResource(key) is SolidColorBrush brush)
        {
            var c = brush.Color;
            return new(c.R, c.G, c.B);
        }

        return fallback;
    }

    private static Color ToMedia(DColor color) => Color.FromRgb(color.R, color.G, color.B);

    private enum NodeRole
    {
        Reader = 0,
        Writer = 1,
        Transit = 2,
        Isolated = 3,
    }

    private sealed record ThemePalette(
        DColor PanelBackground,
        DColor NodeLabel,
        DColor Edge,
        DColor EdgeDisabled,
        DColor DisabledFill,
        DColor DisabledOutline,
        DColor DisabledLabel,
        DColor IsolatedFill,
        DColor IsolatedOutline,
        DColor ReaderFill,
        DColor ReaderOutline,
        DColor WriterFill,
        DColor WriterOutline,
        DColor TransitFill,
        DColor TransitOutline)
    {
        public (DColor Fill, DColor Outline) RoleColors(NodeRole role) => role switch
        {
            NodeRole.Reader => (ReaderFill, ReaderOutline),
            NodeRole.Writer => (WriterFill, WriterOutline),
            NodeRole.Transit => (TransitFill, TransitOutline),
            _ => (IsolatedFill, IsolatedOutline),
        };
    }
}
