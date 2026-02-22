using System;
using XamlToCSharpGenerator.NoUi;

namespace NoUiFrameworkPilotSample;

internal static class Program
{
    private static void Main()
    {
        var view = new MainView();
        var graph = view.BuildNoUiObjectGraph();
        Console.WriteLine($"NoUI graph root: {graph.TypeName}");
        Console.WriteLine($"Total nodes: {CountNodes(graph)}");
    }

    private static int CountNodes(NoUiObjectNode node)
    {
        var count = 1;
        foreach (var child in node.Children)
        {
            count += CountNodes(child);
        }

        return count;
    }
}
