using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public sealed class XamlSourceGenStudioHitTestingSelectionTests
{
    [AvaloniaFact]
    public void CollectIdentityCandidates_Logical_Mode_Uses_Logical_Chain_Order()
    {
        var innerButton = new Button { Name = "ActionButton" };
        var namedBorder = new Border { Name = "NamedBorder", Child = innerButton };
        var root = new StackPanel
        {
            Name = "RootPanel",
            Children = { namedBorder }
        };

        var names = new List<string>();
        var types = new List<string>();
        XamlSourceGenStudioHitTestingService.CollectIdentityCandidates(
            innerButton,
            SourceGenHotDesignHitTestMode.Logical,
            names,
            types);

        Assert.Equal(new[] { "ActionButton", "NamedBorder", "RootPanel" }, names);
        Assert.Equal(new[] { "Button", "Border", "StackPanel" }, types);
    }

    [AvaloniaFact]
    public void CollectIdentityCandidates_Visual_Mode_DeDuplicates_Names_And_Types()
    {
        var child = new Button { Name = "SharedName" };
        var parent = new Border { Name = "SharedName", Child = child };
        var root = new StackPanel { Name = "RootPanel", Children = { parent } };
        _ = root; // Keeps hierarchy rooted for ancestor traversal.

        var names = new List<string>();
        var types = new List<string>();
        XamlSourceGenStudioHitTestingService.CollectIdentityCandidates(
            child,
            SourceGenHotDesignHitTestMode.Visual,
            names,
            types);

        Assert.Equal(new[] { "SharedName", "RootPanel" }, names);
        Assert.Equal(new[] { "Button", "Border", "StackPanel" }, types);
    }

    [AvaloniaFact]
    public void TryFindBestMatchingElementNode_Prefers_Name_Match_Over_Type_Match()
    {
        var targetByName = Node(
            id: "1/0",
            typeName: "Border",
            xamlName: "Target",
            depth: 1);
        var deeperTypeMatch = Node(
            id: "1/0/0/0/0",
            typeName: "Button",
            xamlName: "NotTarget",
            depth: 5);

        var flattened = new List<SourceGenHotDesignElementNode>
        {
            Node(id: "0", typeName: "Grid", xamlName: "Root", depth: 0),
            targetByName,
            deeperTypeMatch
        };

        var matched = XamlSourceGenStudioSelectionResolver.TryFindBestMatchingElementNode(
            flattened,
            controlNames: new[] { "Target" },
            controlTypeNames: new[] { "Button" },
            out var selected);

        Assert.True(matched);
        Assert.Same(targetByName, selected);
    }

    [AvaloniaFact]
    public void TryFindBestMatchingElementNode_Selects_Deepest_Name_Match()
    {
        var shallow = Node(
            id: "2/0",
            typeName: "TextBlock",
            xamlName: "NameMatch",
            depth: 1);
        var deep = Node(
            id: "2/0/1",
            typeName: "TextBlock",
            xamlName: "NameMatch",
            depth: 2);

        var flattened = new List<SourceGenHotDesignElementNode>
        {
            Node(id: "2", typeName: "StackPanel", xamlName: "Root", depth: 0),
            shallow,
            deep
        };

        var matched = XamlSourceGenStudioSelectionResolver.TryFindBestMatchingElementNode(
            flattened,
            controlNames: new[] { "NameMatch" },
            controlTypeNames: new[] { "TextBlock" },
            out var selected);

        Assert.True(matched);
        Assert.Same(deep, selected);
    }

    [AvaloniaFact]
    public void ResolveLogicalSelectionControl_Uses_Visual_Ancestors_For_Visual_Only_Child()
    {
        var visualOnlyChild = new Border
        {
            Width = 200,
            Height = 40,
            Name = "VisualOnlyChild",
            Background = Brushes.Transparent
        };
        var host = new VisualOnlyHostControl(visualOnlyChild)
        {
            Width = 200,
            Height = 40
        };

        Assert.DoesNotContain(visualOnlyChild, host.GetLogicalDescendants().OfType<Control>());

        var resolved = XamlSourceGenStudioHitTestingService.ResolveLogicalSelectionControl(visualOnlyChild, host);

        Assert.Same(host, resolved);
    }

    [AvaloniaFact]
    public void TryGetControlBoundsInLayer_Uses_Control_Local_Coordinates()
    {
        var target = new Border
        {
            Width = 120,
            Height = 30,
            Margin = new Thickness(40, 60, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var liveLayer = new Grid
        {
            Width = 320,
            Height = 200,
            Children = { target }
        };
        var window = new Window
        {
            Width = 320,
            Height = 200,
            Content = liveLayer
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var resolved = XamlSourceGenStudioHitTestingService.TryGetControlBoundsInLayer(target, liveLayer, out var bounds);

            Assert.True(resolved);
            Assert.Equal(40, bounds.X);
            Assert.Equal(60, bounds.Y);
            Assert.Equal(120, bounds.Width);
            Assert.Equal(30, bounds.Height);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    private static SourceGenHotDesignElementNode Node(string id, string typeName, string? xamlName, int depth)
    {
        return new SourceGenHotDesignElementNode(
            Id: id,
            DisplayName: typeName,
            TypeName: typeName,
            XamlName: xamlName,
            Classes: null,
            Depth: depth,
            IsSelected: false,
            Line: 0,
            Children: new List<SourceGenHotDesignElementNode>());
    }

    private sealed class VisualOnlyHostControl : Control
    {
        private readonly Control _visualOnlyChild;

        public VisualOnlyHostControl(Control visualOnlyChild)
        {
            _visualOnlyChild = visualOnlyChild;
            VisualChildren.Add(visualOnlyChild);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _visualOnlyChild.Measure(availableSize);
            return _visualOnlyChild.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _visualOnlyChild.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
