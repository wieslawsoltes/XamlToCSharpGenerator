using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform;
using SourceGenXamlCatalogSample.Infrastructure;
using XamlToCSharpGenerator.Runtime;

namespace SourceGenXamlCatalogSample.ViewModels;

public sealed class RuntimeLoaderPageViewModel : ViewModelBase
{
    private const string AssetUri = "avares://SourceGenXamlCatalogSample/Assets/RuntimeLoaderCard.xml";

    private string _status = "Press a button to parse and load XAML at runtime.";
    private string _inlineXaml = """
        <Border xmlns="https://github.com/avaloniaui"
                Padding="12"
                CornerRadius="8"
                BorderBrush="#405A6A"
                BorderThickness="1"
                Background="#142D7FF9">
          <StackPanel Spacing="6">
            <TextBlock FontWeight="SemiBold" Text="Inline Runtime XAML" />
            <TextBlock Text="This control was created from InlineXaml in RuntimeLoaderPageViewModel." />
          </StackPanel>
        </Border>
        """;
    private object? _loadedContent;

    public RuntimeLoaderPageViewModel()
    {
        LoadInlineCommand = new RelayCommand(LoadInline);
        LoadAssetCommand = new RelayCommand(LoadAsset);
    }

    public RelayCommand LoadInlineCommand { get; }

    public RelayCommand LoadAssetCommand { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string InlineXaml
    {
        get => _inlineXaml;
        set => SetProperty(ref _inlineXaml, value);
    }

    public object? LoadedContent
    {
        get => _loadedContent;
        private set => SetProperty(ref _loadedContent, value);
    }

    private void LoadInline()
    {
        try
        {
            var loaded = AvaloniaSourceGeneratedXamlLoader.Load(
                InlineXaml,
                localAssemblyAnchorType: typeof(RuntimeLoaderPageViewModel),
                baseUri: new Uri("avares://SourceGenXamlCatalogSample/Runtime/Inline.axaml"));

            LoadedContent = EnsureControl(loaded);
            Status = "Inline runtime load succeeded.";
        }
        catch (Exception ex)
        {
            LoadedContent = BuildFailureControl(ex.Message);
            Status = "Inline runtime load failed: " + ex.GetType().Name;
        }
    }

    private void LoadAsset()
    {
        try
        {
            var uri = new Uri(AssetUri);
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var xaml = reader.ReadToEnd();

            var loaded = AvaloniaSourceGeneratedXamlLoader.Load(
                xaml,
                localAssemblyAnchorType: typeof(RuntimeLoaderPageViewModel),
                baseUri: uri);

            LoadedContent = EnsureControl(loaded);
            Status = "Asset runtime load succeeded (" + AssetUri + ").";
        }
        catch (Exception ex)
        {
            LoadedContent = BuildFailureControl(ex.Message);
            Status = "Asset runtime load failed: " + ex.GetType().Name;
        }
    }

    private static Control EnsureControl(object? loaded)
    {
        if (loaded is Control control)
        {
            return control;
        }

        return new TextBlock
        {
            Text = "Runtime load produced non-control: " + (loaded?.GetType().FullName ?? "<null>")
        };
    }

    private static Control BuildFailureControl(string message)
    {
        return new Border
        {
            BorderThickness = new Avalonia.Thickness(1),
            BorderBrush = Avalonia.Media.Brushes.IndianRed,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(10),
            Child = new TextBlock
            {
                Text = "Runtime load error: " + message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        };
    }
}
