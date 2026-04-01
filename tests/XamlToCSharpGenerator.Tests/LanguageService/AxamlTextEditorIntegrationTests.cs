using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit.Folding;
using XamlToCSharpGenerator.Editor.Avalonia;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class AxamlTextEditorIntegrationTests
{
    [AvaloniaFact(Timeout = 15000)]
    public async Task Editor_Renders_And_UpdatesDiagnostics_OnTextChange()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        var editor = new AxamlTextEditor(engine)
        {
            DocumentUri = "file:///tmp/EditorIntegration.axaml",
            WorkspaceRoot = "/tmp",
            Text = "<UserControl>\n  <Button>\n</UserControl>"
        };

        var window = new Window
        {
            Width = 640,
            Height = 480,
            Content = editor
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(window.CaptureRenderedFrame());

        var hasInitialDiagnostics = await WaitForAsync(
            predicate: () => !editor.Diagnostics.IsDefaultOrEmpty,
            timeout: TimeSpan.FromSeconds(5));
        Assert.True(hasInitialDiagnostics);
        var hasFoldings = await WaitForAsync(
            predicate: () =>
            {
                var foldingManager = GetFoldingManager(editor);
                return foldingManager is not null && foldingManager.AllFoldings.Any();
            },
            timeout: TimeSpan.FromSeconds(5));
        Assert.True(hasFoldings);
        var hasTextMateTokens = await WaitForAsync(
            predicate: () => GetTextMateTokenCount(editor, lineIndex: 0) > 0,
            timeout: TimeSpan.FromSeconds(5));
        Assert.True(hasTextMateTokens);
        var initialDiagnostics = editor.Diagnostics;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            editor.Text = "<UserControl xmlns=\"https://github.com/avaloniaui\"><Button/></UserControl>";
        });
        Dispatcher.UIThread.RunJobs();

        var diagnosticsUpdated = await WaitForAsync(
            predicate: () => !AreDiagnosticsEquivalent(initialDiagnostics, editor.Diagnostics),
            timeout: TimeSpan.FromSeconds(5));
        Assert.True(diagnosticsUpdated);
        Assert.DoesNotContain(editor.Diagnostics, static diagnostic => diagnostic.Severity == LanguageServiceDiagnosticSeverity.Error);

        await Dispatcher.UIThread.InvokeAsync(window.Close);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < timeout)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        return predicate();
    }

    private static bool AreDiagnosticsEquivalent(
        System.Collections.Immutable.ImmutableArray<LanguageServiceDiagnostic> left,
        System.Collections.Immutable.ImmutableArray<LanguageServiceDiagnostic> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            var a = left[i];
            var b = right[i];
            if (!string.Equals(a.Code, b.Code, StringComparison.Ordinal) ||
                !string.Equals(a.Message, b.Message, StringComparison.Ordinal) ||
                a.Range != b.Range ||
                a.Severity != b.Severity)
            {
                return false;
            }
        }

        return true;
    }

    private static FoldingManager? GetFoldingManager(AxamlTextEditor editor)
    {
        var foldingSupportField = typeof(AxamlTextEditor).GetField(
            "_foldingSupport",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(foldingSupportField);

        var foldingSupport = foldingSupportField!.GetValue(editor);
        Assert.NotNull(foldingSupport);

        var foldingManagerField = foldingSupport!.GetType().GetField(
            "_foldingManager",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(foldingManagerField);

        return Assert.IsAssignableFrom<FoldingManager>(foldingManagerField!.GetValue(foldingSupport));
    }

    private static int GetTextMateTokenCount(AxamlTextEditor editor, int lineIndex)
    {
        var textMateSupportField = typeof(AxamlTextEditor).GetField(
            "_textMateSupport",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(textMateSupportField);

        var textMateSupport = textMateSupportField!.GetValue(editor);
        Assert.NotNull(textMateSupport);

        var installationField = textMateSupport!.GetType().GetField(
            "_installation",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(installationField);

        var installation = installationField!.GetValue(textMateSupport);
        Assert.NotNull(installation);

        var tmModelField = installation!.GetType().GetField(
            "_tmModel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(tmModelField);

        var tmModel = tmModelField!.GetValue(installation);
        if (tmModel is null)
        {
            return 0;
        }

        var getLineTokens = tmModel.GetType().GetMethod("GetLineTokens", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(getLineTokens);

        var tokens = getLineTokens!.Invoke(tmModel, [lineIndex]) as System.Collections.ICollection;
        return tokens?.Count ?? 0;
    }
}
