using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.Editor.Avalonia;

public sealed class AxamlTextEditor : TextEditor
{
    public static readonly DirectProperty<AxamlTextEditor, string> TextProperty =
        AvaloniaProperty.RegisterDirect<AxamlTextEditor, string>(
            nameof(Text),
            editor => editor.Text,
            (editor, value) => editor.Text = value ?? string.Empty);

    public static readonly StyledProperty<string?> DocumentUriProperty =
        AvaloniaProperty.Register<AxamlTextEditor, string?>(nameof(DocumentUri));

    public static readonly StyledProperty<string?> WorkspaceRootProperty =
        AvaloniaProperty.Register<AxamlTextEditor, string?>(nameof(WorkspaceRoot));

    public static readonly DirectProperty<AxamlTextEditor, ImmutableArray<LanguageServiceDiagnostic>> DiagnosticsProperty =
        AvaloniaProperty.RegisterDirect<AxamlTextEditor, ImmutableArray<LanguageServiceDiagnostic>>(
            nameof(Diagnostics),
            editor => editor.Diagnostics);

    private readonly XamlLanguageServiceEngine _engine;
    private readonly AxamlDiagnosticColorizer _diagnosticColorizer;
    private CompletionWindow? _completionWindow;
    private DispatcherTimer? _analysisDebounce;
    private bool _documentOpened;
    private string? _openedDocumentUri;
    private string _text = string.Empty;
    private int _documentVersion;
    private ImmutableArray<LanguageServiceDiagnostic> _diagnostics = ImmutableArray<LanguageServiceDiagnostic>.Empty;
    private CancellationTokenSource? _analysisCts;

    public AxamlTextEditor()
        : this(new XamlLanguageServiceEngine())
    {
    }

    public AxamlTextEditor(XamlLanguageServiceEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _diagnosticColorizer = new AxamlDiagnosticColorizer();

        ShowLineNumbers = true;
        Options.EnableHyperlinks = false;

        TextChanged += OnEditorTextChanged;
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);

        TextArea.TextEntered += OnTextEntered;
        TextArea.TextEntering += OnTextEntering;
        TextArea.TextView.LineTransformers.Add(_diagnosticColorizer);

        _analysisDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _analysisDebounce.Tick += OnAnalysisDebounceTick;

    }

    public new string Text
    {
        get => _text;
        set => SetEditorText(value ?? string.Empty);
    }

    public string? DocumentUri
    {
        get => GetValue(DocumentUriProperty);
        set => SetValue(DocumentUriProperty, value);
    }

    public string? WorkspaceRoot
    {
        get => GetValue(WorkspaceRootProperty);
        set => SetValue(WorkspaceRootProperty, value);
    }

    public ImmutableArray<LanguageServiceDiagnostic> Diagnostics
    {
        get => _diagnostics;
        private set
        {
            SetAndRaise(DiagnosticsProperty, ref _diagnostics, value);
            _diagnosticColorizer.UpdateDiagnostics(value);
            TextArea.TextView.InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = EnsureDocumentOpenAndAnalyzeAsync();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentUriProperty)
        {
            _ = EnsureDocumentOpenAndAnalyzeAsync();
            return;
        }

        if (change.Property == WorkspaceRootProperty && _documentOpened)
        {
            _ = AnalyzeNowAsync();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = null;

        if (!string.IsNullOrWhiteSpace(_openedDocumentUri))
        {
            _engine.CloseDocument(_openedDocumentUri);
        }

        _documentOpened = false;
        _openedDocumentUri = null;
        _completionWindow?.Close();
        _completionWindow = null;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        var currentText = base.Text ?? string.Empty;
        if (!string.Equals(_text, currentText, StringComparison.Ordinal))
        {
            SetAndRaise(TextProperty, ref _text, currentText);
        }

        _analysisDebounce?.Stop();
        _analysisDebounce?.Start();
    }

    private void OnAnalysisDebounceTick(object? sender, EventArgs e)
    {
        _analysisDebounce?.Stop();
        _ = AnalyzeNowAsync();
    }

    private void SetEditorText(string value)
    {
        if (string.Equals(_text, value, StringComparison.Ordinal) &&
            string.Equals(base.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        base.Text = value;
        SetAndRaise(TextProperty, ref _text, value);
    }

    private async Task EnsureDocumentOpenAndAnalyzeAsync()
    {
        var currentDocumentUri = DocumentUri;
        if (string.IsNullOrWhiteSpace(currentDocumentUri))
        {
            CloseOpenedDocument();
            return;
        }

        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();

        if (!string.Equals(_openedDocumentUri, currentDocumentUri, StringComparison.Ordinal))
        {
            CloseOpenedDocument();
        }

        if (!_documentOpened)
        {
            _documentVersion = 1;
            var diagnostics = await _engine.OpenDocumentAsync(
                currentDocumentUri,
                Text ?? string.Empty,
                version: _documentVersion,
                CreateOptions(),
                _analysisCts.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => Diagnostics = diagnostics);
            _documentOpened = true;
            _openedDocumentUri = currentDocumentUri;
            return;
        }

        await AnalyzeNowAsync().ConfigureAwait(false);
    }

    private async Task AnalyzeNowAsync()
    {
        var currentDocumentUri = DocumentUri;
        if (string.IsNullOrWhiteSpace(currentDocumentUri))
        {
            CloseOpenedDocument();
            return;
        }

        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();

        var diagnostics = await _engine.UpdateDocumentAsync(
            currentDocumentUri,
            Text ?? string.Empty,
            version: ++_documentVersion,
            CreateOptions(),
            _analysisCts.Token).ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(() => Diagnostics = diagnostics);
        _documentOpened = true;
        _openedDocumentUri = currentDocumentUri;
    }

    private async Task ShowCompletionAsync()
    {
        if (string.IsNullOrWhiteSpace(DocumentUri) || Document is null)
        {
            return;
        }

        var position = ToSourcePosition(CaretOffset, Document);
        var completionItems = await _engine.GetCompletionsAsync(
            DocumentUri!,
            position,
            CreateOptions(),
            CancellationToken.None).ConfigureAwait(false);

        if (completionItems.IsDefaultOrEmpty)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _completionWindow?.Close();
            _completionWindow = new CompletionWindow(TextArea);
            var data = _completionWindow.CompletionList.CompletionData;
            foreach (var completionItem in completionItems)
            {
                data.Add(new AxamlCompletionData(completionItem));
            }

            _completionWindow.Show();
            _completionWindow.Closed += (_, _) => _completionWindow = null;
        });
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = ShowCompletionAsync();
            e.Handled = true;
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var trigger = e.Text[0];
        if (trigger is '<' or ':' or '.' or '{' or ' ')
        {
            _ = ShowCompletionAsync();
        }
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (_completionWindow?.CompletionList.SelectedItem is null)
        {
            return;
        }

        if (char.IsLetterOrDigit(e.Text[0]))
        {
            return;
        }

        _completionWindow.CompletionList.RequestInsertion(e);
    }

    private XamlLanguageServiceOptions CreateOptions()
    {
        return new XamlLanguageServiceOptions(WorkspaceRoot);
    }

    private void CloseOpenedDocument()
    {
        if (!string.IsNullOrWhiteSpace(_openedDocumentUri))
        {
            _engine.CloseDocument(_openedDocumentUri);
        }

        _documentOpened = false;
        _openedDocumentUri = null;
    }

    private static SourcePosition ToSourcePosition(int offset, TextDocument document)
    {
        var boundedOffset = Math.Max(0, Math.Min(offset, document.TextLength));
        var line = document.GetLineByOffset(boundedOffset);
        var lineIndex = line.LineNumber - 1;
        var character = boundedOffset - line.Offset;
        return new SourcePosition(lineIndex, character);
    }
}
