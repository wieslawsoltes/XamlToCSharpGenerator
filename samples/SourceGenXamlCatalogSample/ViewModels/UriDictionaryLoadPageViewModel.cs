using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SourceGenXamlCatalogSample.Infrastructure;

namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class UriDictionaryLoadPageViewModel : ViewModelBase
{
    private const string DictionaryUri = "avares://SourceGenXamlCatalogSample/Resources/UriLoadedIssueDictionary.axaml";

    private string _status = "Press reload to load a classless ResourceDictionary through AvaloniaXamlLoader.Load(Uri).";
    private string _loadedTypeName = "<not loaded>";
    private object? _previewContent;

    public UriDictionaryLoadPageViewModel()
    {
        ReloadCommand = new RelayCommand(Reload);
        Reload();
    }

    public RelayCommand ReloadCommand { get; }

    public string DictionarySourceUri => DictionaryUri;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string LoadedTypeName
    {
        get => _loadedTypeName;
        private set => SetProperty(ref _loadedTypeName, value);
    }

    public object? PreviewContent
    {
        get => _previewContent;
        private set => SetProperty(ref _previewContent, value);
    }

    private void Reload()
    {
        try
        {
            var loaded = AvaloniaXamlLoader.Load(new Uri(DictionaryUri));
            if (loaded is not ResourceDictionary dictionary)
            {
                LoadedTypeName = loaded?.GetType().FullName ?? "<null>";
                PreviewContent = BuildFailureCard("Expected ResourceDictionary but got " + LoadedTypeName + ".");
                Status = "URI load returned the wrong root type.";
                return;
            }

            LoadedTypeName = dictionary.GetType().FullName ?? nameof(ResourceDictionary);
            PreviewContent = BuildPreviewCard(dictionary);
            Status = "AvaloniaXamlLoader.Load(Uri) returned a classless ResourceDictionary and exposed its keyed values.";
        }
        catch (Exception ex)
        {
            LoadedTypeName = ex.GetType().FullName ?? ex.GetType().Name;
            PreviewContent = BuildFailureCard(ex.Message);
            Status = "URI load failed: " + ex.GetType().Name;
        }
    }

    private static Control BuildPreviewCard(ResourceDictionary dictionary)
    {
        var headline = ReadString(dictionary, "Issue36.Headline");
        var body = ReadString(dictionary, "Issue36.Body");
        var accentBrush = ReadBrush(dictionary, "Issue36.AccentBrush");

        return new Border
        {
            Background = accentBrush,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                        Text = headline
                    },
                    new TextBlock
                    {
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap,
                        Text = body
                    }
                }
            }
        };
    }

    private static Control BuildFailureCard(string message)
    {
        return new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.IndianRed,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = "Load error: " + message,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private static string ReadString(ResourceDictionary dictionary, string key)
    {
        return dictionary.TryGetValue(key, out var value) && value is string text
            ? text
            : "<missing string: " + key + ">";
    }

    private static IBrush ReadBrush(ResourceDictionary dictionary, string key)
    {
        return dictionary.TryGetValue(key, out var value) && value is IBrush brush
            ? brush
            : Brushes.Gray;
    }
}
