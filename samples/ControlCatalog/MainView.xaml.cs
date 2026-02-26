using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Avalonia.Threading;
using ControlCatalog.Pages;
using ControlCatalog.ViewModels;

namespace ControlCatalog
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent(true);

            var sideBar = this.Get<TabControl>("Sidebar");
            var themeVariants = this.Get<ComboBox>("ThemeVariants");
            themeVariants.SelectedItem = Application.Current!.RequestedThemeVariant;
            themeVariants.SelectionChanged += (sender, e) =>
            {
                if (themeVariants.SelectedItem is ThemeVariant themeVariant)
                {
                    Application.Current!.RequestedThemeVariant = themeVariant;
                }
            };

            var flowDirections = this.Get<ComboBox>("FlowDirectionSelector");
            flowDirections.SelectionChanged += (sender, e) =>
            {
                if (flowDirections.SelectedItem is FlowDirection flowDirection)
                {
                    TopLevel.GetTopLevel(this)!.FlowDirection = flowDirection;
                }
            };

            var decorations = this.Get<ComboBox>("Decorations");
            decorations.SelectionChanged += (sender, e) =>
            {
                if (VisualRoot is Window window
                    && decorations.SelectedItem is SystemDecorations systemDecorations)
                {
                    window.SystemDecorations = systemDecorations;
                }
            };

            var transparencyLevels = this.Get<ComboBox>("TransparencyLevels");
            IDisposable? topLevelBackgroundSideSetter = null, sideBarBackgroundSetter = null, paneBackgroundSetter = null;
            transparencyLevels.SelectionChanged += (sender, e) =>
            {
                topLevelBackgroundSideSetter?.Dispose();
                sideBarBackgroundSetter?.Dispose();
                paneBackgroundSetter?.Dispose();
                if (transparencyLevels.SelectedItem is WindowTransparencyLevel selected)
                {
                    var topLevel = (TopLevel)this.GetVisualRoot()!;
                    topLevel.TransparencyLevelHint = new[] { selected };

                    if (topLevel.ActualTransparencyLevel != WindowTransparencyLevel.None &&
                        topLevel.ActualTransparencyLevel == selected)
                    {
                        var transparentBrush = new ImmutableSolidColorBrush(Colors.White, 0);
                        var semiTransparentBrush = new ImmutableSolidColorBrush(Colors.Gray, 0.2);
                        topLevelBackgroundSideSetter = topLevel.SetValue(BackgroundProperty, transparentBrush, Avalonia.Data.BindingPriority.Style);
                        sideBarBackgroundSetter = sideBar.SetValue(BackgroundProperty, semiTransparentBrush, Avalonia.Data.BindingPriority.Style);
                        paneBackgroundSetter = sideBar.SetValue(SplitView.PaneBackgroundProperty, semiTransparentBrush, Avalonia.Data.BindingPriority.Style);
                    }
                }
            };
        }

        internal MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
        
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            Dispatcher.UIThread.Post(() =>
            {
                var sideBar = this.Get<TabControl>("Sidebar");
                var selectedItem = sideBar.SelectedItem as TabItem;
                var selectedHost = sideBar.GetVisualDescendants().OfType<ContentPresenter>()
                    .FirstOrDefault(x => string.Equals(x.Name, "PART_SelectedContentHost", StringComparison.Ordinal));
                var itemsPresenter = sideBar.GetVisualDescendants().OfType<ItemsPresenter>()
                    .FirstOrDefault(x => string.Equals(x.Name, "PART_ItemsPresenter", StringComparison.Ordinal));
                var itemsPanel = itemsPresenter?.Panel;
                var contentDescendants = selectedHost?.GetVisualDescendants().ToList();
                var nestedTabControl = contentDescendants?.OfType<TabControl>()
                    .FirstOrDefault(x => !ReferenceEquals(x, sideBar));

                Console.WriteLine(
                    $"[ControlCatalog.Debug] Sidebar attached: items={sideBar.Items.Count}, selectedIndex={sideBar.SelectedIndex}, selectedItem={selectedItem?.GetType().Name ?? "<null>"}, selectedItemContent={selectedItem?.Content?.GetType().FullName ?? "<null>"}, selectedContent={sideBar.SelectedContent?.GetType().FullName ?? "<null>"}");
                Console.WriteLine(
                    $"[ControlCatalog.Debug] Template parts: selectedHost={(selectedHost is not null)}, selectedHostContent={selectedHost?.Content?.GetType().FullName ?? "<null>"}, selectedHostBounds={selectedHost?.Bounds}, itemsPresenter={(itemsPresenter is not null)}, itemsPanel={itemsPanel?.GetType().FullName ?? "<null>"}, itemsPanelChildren={itemsPanel?.Children.Count ?? -1}");
                Console.WriteLine(
                    $"[ControlCatalog.Debug] Nested tab: exists={(nestedTabControl is not null)}, theme={nestedTabControl?.Theme?.GetType().FullName ?? "<null>"}, templateSet={nestedTabControl?.Template is not null}, itemCount={nestedTabControl?.ItemCount ?? -1}, selectedIndex={nestedTabControl?.SelectedIndex ?? -1}, selectedContent={nestedTabControl?.SelectedContent?.GetType().FullName ?? "<null>"}");
                if (contentDescendants is not null)
                {
                    Console.WriteLine($"[ControlCatalog.Debug] SelectedHost visual descendants={contentDescendants.Count}");
                    foreach (var visual in contentDescendants.Take(5))
                    {
                        Console.WriteLine($"[ControlCatalog.Debug] SelectedHost child: {visual.GetType().FullName} bounds={visual.Bounds}");
                    }
                }
            }, DispatcherPriority.Loaded);

            var decorations = this.Get<ComboBox>("Decorations");
            if (VisualRoot is Window window)
                decorations.SelectedIndex = (int)window.SystemDecorations;

            var insets = TopLevel.GetTopLevel(this)!.InsetsManager;
            if (insets != null)
            {
                // In real life application these events should be unsubscribed to avoid memory leaks.
                ViewModel.SafeAreaPadding = insets.SafeAreaPadding;
                insets.SafeAreaChanged += (sender, args) =>
                {
                    ViewModel.SafeAreaPadding = insets.SafeAreaPadding;
                };

                ViewModel.DisplayEdgeToEdge = insets.DisplayEdgeToEdgePreference;
                ViewModel.IsSystemBarVisible = insets.IsSystemBarVisible ?? true;

                ViewModel.PropertyChanged += async (sender, args) =>
                {
                    if (args.PropertyName == nameof(ViewModel.DisplayEdgeToEdge))
                    {
                        insets.DisplayEdgeToEdgePreference = ViewModel.DisplayEdgeToEdge;
                    }
                    else if (args.PropertyName == nameof(ViewModel.IsSystemBarVisible))
                    {
                        insets.IsSystemBarVisible = ViewModel.IsSystemBarVisible;
                    }

                    // Give the OS some time to apply new values and refresh the view model.
                    await Task.Delay(100);
                    ViewModel.DisplayEdgeToEdge = insets.DisplayEdgeToEdgePreference;
                    ViewModel.IsSystemBarVisible = insets.IsSystemBarVisible ?? true;
                };
            }
        }
    }
}
