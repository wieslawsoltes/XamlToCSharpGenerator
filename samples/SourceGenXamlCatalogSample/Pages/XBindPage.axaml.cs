using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGenXamlCatalogSample.Pages;

public partial class XBindPage : UserControl
{
    public static readonly DirectProperty<XBindPage, string?> EditorDraftProperty =
        AvaloniaProperty.RegisterDirect<XBindPage, string?>(
            nameof(EditorDraft),
            static owner => owner.EditorDraft,
            static (owner, value) => owner.EditorDraft = value);

    public static readonly DirectProperty<XBindPage, string?> AliasProperty =
        AvaloniaProperty.RegisterDirect<XBindPage, string?>(
            nameof(Alias),
            static owner => owner.Alias,
            static (owner, value) => owner.Alias = value);

    public static readonly DirectProperty<XBindPage, string?> SearchDraftProperty =
        AvaloniaProperty.RegisterDirect<XBindPage, string?>(
            nameof(SearchDraft),
            static owner => owner.SearchDraft,
            static (owner, value) => owner.SearchDraft = value);

    public static readonly DirectProperty<XBindPage, string> LastBindBackMessageProperty =
        AvaloniaProperty.RegisterDirect<XBindPage, string>(
            nameof(LastBindBackMessage),
            static owner => owner.LastBindBackMessage,
            static (owner, value) => owner.LastBindBackMessage = value);

    public static readonly DirectProperty<XBindPage, XBindContact?> SelectedContactProperty =
        AvaloniaProperty.RegisterDirect<XBindPage, XBindContact?>(
            nameof(SelectedContact),
            static owner => owner.SelectedContact,
            static (owner, value) => owner.SelectedContact = value);

    public static readonly DirectProperty<XBindPage, string> EventStatusProperty =
        AvaloniaProperty.RegisterDirect<XBindPage, string>(
            nameof(EventStatus),
            static owner => owner.EventStatus,
            static (owner, value) => owner.EventStatus = value);

    public static readonly DirectProperty<XBindPage, int> ClickCountProperty =
        AvaloniaProperty.RegisterDirect<XBindPage, int>(
            nameof(ClickCount),
            static owner => owner.ClickCount,
            static (owner, value) => owner.ClickCount = value);

    private string? _editorDraft = "Inspect named-element x:Bind input";
    private string? _alias = "Catalog alias";
    private string? _searchDraft = " initial search ";
    private string _lastBindBackMessage = "BindBack has not fired yet.";
    private XBindContact? _selectedContact;
    private string _eventStatus = "No x:Bind event invoked yet.";
    private int _clickCount;

    public XBindPage()
    {
        Contacts =
        [
            new XBindContact("ilker demir", "ilker.demir@example.com", "Istanbul", notes: null),
            new XBindContact("Ava Green", "ava.green@example.com", "Warsaw", "Prefers explicit source-generator diagnostics."),
            new XBindContact("Sofia Turner", "sofia.turner@example.com", "London", "Validates root and template scopes separately.")
        ];

        _selectedContact = Contacts[0];
        InitializeComponent();
    }

    public XBindContact[] Contacts { get; }

    public string? EditorDraft
    {
        get => _editorDraft;
        set => SetAndRaise(EditorDraftProperty, ref _editorDraft, value);
    }

    public string? Alias
    {
        get => _alias;
        set => SetAndRaise(AliasProperty, ref _alias, value);
    }

    public string? SearchDraft
    {
        get => _searchDraft;
        set => SetAndRaise(SearchDraftProperty, ref _searchDraft, value);
    }

    public string LastBindBackMessage
    {
        get => _lastBindBackMessage;
        set => SetAndRaise(LastBindBackMessageProperty, ref _lastBindBackMessage, value);
    }

    public XBindContact? SelectedContact
    {
        get => _selectedContact;
        set => SetAndRaise(SelectedContactProperty, ref _selectedContact, value);
    }

    public string EventStatus
    {
        get => _eventStatus;
        set => SetAndRaise(EventStatusProperty, ref _eventStatus, value);
    }

    public int ClickCount
    {
        get => _clickCount;
        set => SetAndRaise(ClickCountProperty, ref _clickCount, value);
    }

    public string FormatPreview(string? editorText, string? selectedName)
    {
        var draft = string.IsNullOrWhiteSpace(editorText) ? "<empty>" : editorText.Trim();
        var name = string.IsNullOrWhiteSpace(selectedName) ? "<none>" : selectedName;
        return XBindSampleHelpers.Prefix + " " + draft + " for " + name + ".";
    }

    public string FormatTemplateSummary(string name, string city)
    {
        var alias = string.IsNullOrWhiteSpace(Alias) ? "<none>" : Alias!;
        return "Root formatter combined " + name + " from " + city + " under alias " + alias + ".";
    }

    public void ApplySearchDraft(string? value)
    {
        SearchDraft = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        var visibleValue = string.IsNullOrEmpty(SearchDraft) ? "<empty>" : SearchDraft;
        LastBindBackMessage =
            "BindBack normalized input to '" +
            visibleValue +
            "' at " +
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
            ".";
    }

    public void ApplySelectedContact(object? value)
    {
        SelectedContact = value as XBindContact;
    }

    public void HandlePrimaryClick()
    {
        ClickCount++;
        SelectedContact = Contacts[ClickCount % Contacts.Length];
        var selectedName = SelectedContact?.Name ?? "<none>";
        EventStatus = "HandlePrimaryClick selected " + selectedName + ".";
    }

    public void HandleDetailedClick(object? sender, RoutedEventArgs args)
    {
        ClickCount++;
        var senderTypeName = sender?.GetType().Name ?? "<null>";
        EventStatus = "HandleDetailedClick received " + senderTypeName + " for routed event '" + args.RoutedEvent.Name + "'.";
    }

    public void CaptureEditorText(string? currentText)
    {
        ClickCount++;
        var visibleText = string.IsNullOrWhiteSpace(currentText) ? "<empty>" : currentText.Trim();
        EventStatus = "Invocation expression captured Editor.Text = '" + visibleText + "'.";
    }
}
