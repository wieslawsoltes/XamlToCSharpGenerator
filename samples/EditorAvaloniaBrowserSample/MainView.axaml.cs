using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EditorAvaloniaBrowserSample;

public sealed partial class MainView : UserControl
{
    private const string SampleXaml = """
                                      <UserControl xmlns="https://github.com/avaloniaui"
                                                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                        <Grid RowDefinitions="Auto,*"
                                              Margin="16">
                                          <TextBlock Text="Browser-hosted AXAML language service"
                                                     FontSize="22"
                                                     FontWeight="SemiBold" />
                                          <StackPanel Grid.Row="1"
                                                      Margin="0,16,0,0"
                                                      Spacing="8">
                                            <Button Content="Run" />
                                            <TextBlock Text="{Binding MissingProperty}" />
                                          </StackPanel>
                                        </Grid>
                                      </UserControl>
                                      """;

    public MainView()
    {
        AvaloniaXamlLoader.Load(this);

        Editor.DocumentUri = "file:///workspace/MainView.axaml";
        Editor.WorkspaceRoot = "/workspace";
        Editor.SourceText = SampleXaml;
    }
}
