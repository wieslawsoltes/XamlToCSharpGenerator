using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SourceGenIlWeavingSample;

public partial class ServiceProviderPanel : UserControl
{
    public ServiceProviderPanel()
        : this(null)
    {
    }

    public ServiceProviderPanel(IServiceProvider? serviceProvider)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);
    }
}
