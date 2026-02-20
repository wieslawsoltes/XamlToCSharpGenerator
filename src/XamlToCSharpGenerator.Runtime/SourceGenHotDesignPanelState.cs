namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotDesignPanelState
{
    public bool ToolbarVisible { get; set; } = true;

    public bool ElementsVisible { get; set; } = true;

    public bool ToolboxVisible { get; set; } = true;

    public bool CanvasVisible { get; set; } = true;

    public bool PropertiesVisible { get; set; } = true;

    public SourceGenHotDesignPanelState Clone()
    {
        return new SourceGenHotDesignPanelState
        {
            ToolbarVisible = ToolbarVisible,
            ElementsVisible = ElementsVisible,
            ToolboxVisible = ToolboxVisible,
            CanvasVisible = CanvasVisible,
            PropertiesVisible = PropertiesVisible
        };
    }

    public bool Toggle(SourceGenHotDesignPanelKind panel)
    {
        switch (panel)
        {
            case SourceGenHotDesignPanelKind.Toolbar:
                ToolbarVisible = !ToolbarVisible;
                return ToolbarVisible;
            case SourceGenHotDesignPanelKind.Elements:
                ElementsVisible = !ElementsVisible;
                return ElementsVisible;
            case SourceGenHotDesignPanelKind.Toolbox:
                ToolboxVisible = !ToolboxVisible;
                return ToolboxVisible;
            case SourceGenHotDesignPanelKind.Canvas:
                CanvasVisible = !CanvasVisible;
                return CanvasVisible;
            case SourceGenHotDesignPanelKind.Properties:
                PropertiesVisible = !PropertiesVisible;
                return PropertiesVisible;
            default:
                return false;
        }
    }

    public void SetVisible(SourceGenHotDesignPanelKind panel, bool visible)
    {
        switch (panel)
        {
            case SourceGenHotDesignPanelKind.Toolbar:
                ToolbarVisible = visible;
                break;
            case SourceGenHotDesignPanelKind.Elements:
                ElementsVisible = visible;
                break;
            case SourceGenHotDesignPanelKind.Toolbox:
                ToolboxVisible = visible;
                break;
            case SourceGenHotDesignPanelKind.Canvas:
                CanvasVisible = visible;
                break;
            case SourceGenHotDesignPanelKind.Properties:
                PropertiesVisible = visible;
                break;
        }
    }
}
