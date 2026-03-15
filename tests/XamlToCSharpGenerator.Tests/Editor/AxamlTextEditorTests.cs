using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using XamlToCSharpGenerator.Editor.Avalonia;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.Tests.Editor;

public class AxamlTextEditorTests
{
    [AvaloniaFact]
    public void TextProperty_Supports_TwoWay_Binding()
    {
        var host = new TestBindingHost
        {
            Text = "<Grid xmlns=\"https://github.com/avaloniaui\" />"
        };
        var editor = new AxamlTextEditor
        {
            DataContext = host
        };
        editor.Bind(AxamlTextEditor.TextProperty, new Binding(nameof(TestBindingHost.Text), BindingMode.TwoWay));

        Assert.Equal(host.Text, editor.Text);

        host.Text = "<Grid xmlns=\"https://github.com/avaloniaui\"><TextBlock Text=\"Bound\" /></Grid>";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(host.Text, editor.Text);

        editor.Text = "<Grid xmlns=\"https://github.com/avaloniaui\"><Button Content=\"Editor\" /></Grid>";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(editor.Text, host.Text);
    }

    [AvaloniaFact]
    public async Task DocumentUri_Change_Closes_Previous_Document_And_Opens_New_One()
    {
        var firstPath = CreateTempXamlFile("Editor.First.axaml");
        var secondPath = CreateTempXamlFile("Editor.Second.axaml");
        using var engine = new XamlLanguageServiceEngine(new StubCompilationProvider());
        var editor = new AxamlTextEditor(engine)
        {
            Text = "<Grid xmlns=\"https://github.com/avaloniaui\"><TextBlock Text=\"First\" /></Grid>"
        };
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = editor
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            editor.WorkspaceRoot = Path.GetDirectoryName(firstPath);
            editor.DocumentUri = new Uri(firstPath).AbsoluteUri;
            await WaitForConditionAsync(() =>
            {
                var store = GetDocumentStore(engine);
                return store.Get(new Uri(firstPath).AbsoluteUri) is not null;
            });

            editor.Text = "<Grid xmlns=\"https://github.com/avaloniaui\"><TextBlock Text=\"Second\" /></Grid>";
            editor.WorkspaceRoot = Path.GetDirectoryName(secondPath);
            editor.DocumentUri = new Uri(secondPath).AbsoluteUri;
            await WaitForConditionAsync(() =>
            {
                var store = GetDocumentStore(engine);
                return store.Get(new Uri(firstPath).AbsoluteUri) is null &&
                       store.Get(new Uri(secondPath).AbsoluteUri) is not null;
            });
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }

            DeleteFileIfExists(firstPath);
            DeleteFileIfExists(secondPath);
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail("Timed out waiting for editor analysis state.");
    }

    private static XamlDocumentStore GetDocumentStore(XamlLanguageServiceEngine engine)
    {
        var field = typeof(XamlLanguageServiceEngine).GetField(
            "_documentStore",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<XamlDocumentStore>(field!.GetValue(engine));
    }

    private static string CreateTempXamlFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var path = Path.Combine(
            Path.GetTempPath(),
            stem + "-" + Guid.NewGuid().ToString("N") + (string.IsNullOrWhiteSpace(extension) ? ".axaml" : extension));
        File.WriteAllText(path, "<Grid xmlns=\"https://github.com/avaloniaui\" />");
        return path;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class StubCompilationProvider : ICompilationProvider
    {
        public Task<CompilationSnapshot> GetCompilationAsync(string filePath, string? workspaceRoot, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CompilationSnapshot(
                ProjectPath: null,
                Project: null,
                Compilation: null,
                Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty));
        }

        public void Invalidate(string filePath)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestBindingHost : INotifyPropertyChanged
    {
        private string _text = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Text
        {
            get => _text;
            set
            {
                if (string.Equals(_text, value, StringComparison.Ordinal))
                {
                    return;
                }

                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }
    }
}
