using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using XamlToCSharpGenerator.Runtime;

namespace Avalonia.Themes.Fluent
{
    public enum DensityStyle
    {
        Normal,
        Compact
    }

    /// <summary>
    /// Includes the fluent theme in an application.
    /// </summary>
    public partial class FluentTheme : Styles, IResourceNode
    {
        private static readonly Uri FluentThemeBaseUri = new("avares://Avalonia.Themes.Fluent/FluentTheme.xaml");
        private readonly object _compactStyles;
        private DensityStyle _densityStyle;

        /// <summary>
        /// Initializes a new instance of the <see cref="FluentTheme"/> class.
        /// </summary>
        /// <param name="sp">The parent's service provider.</param>
        public FluentTheme(IServiceProvider? sp = null)
        {
            InitializeComponent();
            
            _compactStyles = ResolveCompactStyles(GetAndRemove("CompactStyles"));

            Palettes = Resources.MergedDictionaries.OfType<ColorPaletteResourcesCollection>().FirstOrDefault()
                ?? throw new InvalidOperationException("FluentTheme was initialized with missing ColorPaletteResourcesCollection.");
            
            object GetAndRemove(string key)
            {
                var val = Resources[key]
                          ?? throw new KeyNotFoundException($"Key {key} was not found in the resources");
                Resources.Remove(key);
                return val;
            }
        }

        private static object ResolveCompactStyles(object value)
        {
            if (value is not ResourceInclude include)
            {
                return value;
            }

            if (TryResolveSourceGeneratedInclude(include, out var sourceGeneratedValue))
            {
                return sourceGeneratedValue;
            }

            try
            {
                return include.Loaded;
            }
            catch (InvalidOperationException)
            {
                if (include.Source is null)
                {
                    throw;
                }

                // Rehydrate include with explicit BaseUri when upstream construction did not provide one.
                var rebasedInclude = new ResourceInclude(FluentThemeBaseUri)
                {
                    Source = include.Source
                };

                return rebasedInclude.Loaded;
            }
        }

        private static bool TryResolveSourceGeneratedInclude(ResourceInclude include, out object resolved)
        {
            resolved = default!;

            if (include.Source is null)
            {
                return false;
            }

            Uri includeUri;
            if (include.Source.IsAbsoluteUri)
            {
                includeUri = include.Source;
            }
            else
            {
                includeUri = new Uri(FluentThemeBaseUri, include.Source);
            }

            if (!AvaloniaSourceGeneratedXamlLoader.TryLoad(serviceProvider: null, includeUri, out var loaded) ||
                loaded is null)
            {
                return false;
            }

            resolved = loaded;
            return true;
        }

        public static readonly DirectProperty<FluentTheme, DensityStyle> DensityStyleProperty = AvaloniaProperty.RegisterDirect<FluentTheme, DensityStyle>(
            nameof(DensityStyle), o => o.DensityStyle, (o, v) => o.DensityStyle = v);

        /// <summary>
        /// Gets or sets the density style of the fluent theme (normal, compact).
        /// </summary>
        public DensityStyle DensityStyle
        {
            get => _densityStyle;
            set => SetAndRaise(DensityStyleProperty, ref _densityStyle, value);
        }

        public IDictionary<ThemeVariant, ColorPaletteResources> Palettes { get; }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == DensityStyleProperty)
            {
                Owner?.NotifyHostedResourcesChanged(ResourcesChangedEventArgs.Empty);
            }
        }

        bool IResourceNode.TryGetResource(object key, ThemeVariant? theme, out object? value)
        {
            // DensityStyle dictionary should be checked first
            if (_densityStyle == DensityStyle.Compact
                && _compactStyles is IResourceProvider compactStyleProvider
                && compactStyleProvider.TryGetResource(key, theme, out value))
            {
                return true;
            }

            return base.TryGetResource(key, theme, out value);
        }

#if !AXAML_SOURCEGEN_BACKEND
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
#endif
    }
}
