using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class XamlSourceGenHotDesignDocumentEditorTests
{
    [Fact]
    public void Property_Update_Preserves_Existing_Attribute_Formatting()
    {
        const string original =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button
                  x:Name='ActionButton'
                  Content = "Run"
                  Width = '120' />
            </UserControl>
            """;
        const string expected =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button
                  x:Name='ActionButton'
                  Content = "Updated"
                  Width = '120' />
            </UserControl>
            """;

        Assert.True(XamlSourceGenHotDesignDocumentEditor.TryCreate(original, out var editor, out var error), error);
        Assert.NotNull(editor);

        var updated = editor!.TryApplyPropertyUpdate(
            "0/0",
            "Content",
            "Updated",
            removeProperty: false,
            out var updatedText,
            out var updateError);

        Assert.True(updated, updateError);
        Assert.Equal(expected, updatedText);
    }

    [Fact]
    public void Property_Remove_Preserves_Multiline_Attribute_Layout()
    {
        const string original =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button
                  x:Name="ActionButton"
                  Width="120"
                  Height="30" />
            </UserControl>
            """;
        const string expected =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Button
                  x:Name="ActionButton"
                  Height="30" />
            </UserControl>
            """;

        Assert.True(XamlSourceGenHotDesignDocumentEditor.TryCreate(original, out var editor, out var error), error);
        Assert.NotNull(editor);

        var removed = editor!.TryApplyPropertyUpdate(
            "0/0",
            "Width",
            propertyValue: null,
            removeProperty: true,
            out var updatedText,
            out var updateError);

        Assert.True(removed, updateError);
        Assert.Equal(expected, updatedText);
    }

    [Fact]
    public void Insert_Element_Preserves_Sibling_Indentation()
    {
        const string original =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel>
                <TextBlock Text="Hello" />
              </StackPanel>
            </UserControl>
            """;
        const string expected =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel>
                <TextBlock Text="Hello" />
                <Button />
              </StackPanel>
            </UserControl>
            """;

        Assert.True(XamlSourceGenHotDesignDocumentEditor.TryCreate(original, out var editor, out var error), error);
        Assert.NotNull(editor);

        var inserted = editor!.TryInsertElement(
            "0/0",
            "Button",
            xamlFragment: null,
            out var updatedText,
            out var updateError);

        Assert.True(inserted, updateError);
        Assert.Equal(expected, updatedText);
    }

    [Fact]
    public void Remove_Element_Preserves_Surrounding_Whitespace()
    {
        const string original =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel>
                <TextBlock Text="Hello" />
                <Button Content="Run" />
              </StackPanel>
            </UserControl>
            """;
        const string expected =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel>
                <Button Content="Run" />
              </StackPanel>
            </UserControl>
            """;

        Assert.True(XamlSourceGenHotDesignDocumentEditor.TryCreate(original, out var editor, out var error), error);
        Assert.NotNull(editor);

        var removed = editor!.TryRemoveElement("0/0/0", out var updatedText, out var updateError);

        Assert.True(removed, updateError);
        Assert.Equal(expected, updatedText);
    }

    [Fact]
    public void Insert_Element_Expands_SelfClosing_Parent_With_Local_Indentation()
    {
        const string original =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel />
            </UserControl>
            """;
        const string expected =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel>
                <Button />
              </StackPanel>
            </UserControl>
            """;

        Assert.True(XamlSourceGenHotDesignDocumentEditor.TryCreate(original, out var editor, out var error), error);
        Assert.NotNull(editor);

        var inserted = editor!.TryInsertElement(
            "0/0",
            "Button",
            xamlFragment: null,
            out var updatedText,
            out var updateError);

        Assert.True(inserted, updateError);
        Assert.Equal(expected, updatedText);
    }

    [Fact]
    public void Insert_Element_Preserves_Inline_Container_Layout()
    {
        const string original =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel><TextBlock Text="Hello" /></StackPanel>
            </UserControl>
            """;
        const string expected =
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel><TextBlock Text="Hello" /><Button /></StackPanel>
            </UserControl>
            """;

        Assert.True(XamlSourceGenHotDesignDocumentEditor.TryCreate(original, out var editor, out var error), error);
        Assert.NotNull(editor);

        var inserted = editor!.TryInsertElement(
            "0/0",
            "Button",
            xamlFragment: null,
            out var updatedText,
            out var updateError);

        Assert.True(inserted, updateError);
        Assert.Equal(expected, updatedText);
    }

    [Fact]
    public void Insert_Element_Uses_Tab_Indentation_When_Source_Uses_Tabs()
    {
        const string original = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n\txmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n\t<StackPanel />\n</UserControl>";
        const string expected = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n\txmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n\t<StackPanel>\n\t\t<Button />\n\t</StackPanel>\n</UserControl>";

        Assert.True(XamlSourceGenHotDesignDocumentEditor.TryCreate(original, out var editor, out var error), error);
        Assert.NotNull(editor);

        var inserted = editor!.TryInsertElement(
            "0/0",
            "Button",
            xamlFragment: null,
            out var updatedText,
            out var updateError);

        Assert.True(inserted, updateError);
        Assert.Equal(expected, updatedText);
    }
}
