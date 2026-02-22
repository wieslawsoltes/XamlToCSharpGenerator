using System.Collections.Generic;

namespace NoUiFrameworkPilotSample.Controls;

public class Page
{
    public object? Content { get; set; }
}

public sealed class StackPanel
{
    public List<object> Children { get; } = [];
}

public sealed class Label
{
    public string? Text { get; set; }
}

public sealed class Button
{
    public string? Content { get; set; }

    public string? CommandName { get; set; }
}
