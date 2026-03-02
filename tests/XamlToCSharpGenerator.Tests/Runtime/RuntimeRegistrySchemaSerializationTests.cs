using System;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeRegistry")]
public class RuntimeRegistrySchemaSerializationTests
{
    [Fact]
    public void Resource_Registry_Serializes_And_Restores_Descriptor()
    {
        const string uri = "avares://Demo/Resources.axaml";
        var descriptor = new SourceGenResourceDescriptor(uri, "AccentBrush", "Brush", "<SolidColorBrush />");
        XamlResourceRegistry.Clear(uri);

        var payload = XamlResourceRegistry.Serialize(descriptor);

        Assert.True(XamlResourceRegistry.TryRegisterSerialized(payload));
        var restored = Assert.Single(XamlResourceRegistry.GetAll(uri));
        Assert.Equal(descriptor, restored);
    }

    [Fact]
    public void Style_Registry_Serializes_And_Restores_Descriptor()
    {
        const string uri = "avares://Demo/Styles.axaml";
        var descriptor = new SourceGenStyleDescriptor(uri, "PrimaryButton", "Button.primary", "global::Avalonia.Controls.Button", "<Style />");
        XamlStyleRegistry.Clear(uri);

        var payload = XamlStyleRegistry.Serialize(descriptor);

        Assert.True(XamlStyleRegistry.TryRegisterSerialized(payload));
        var restored = Assert.Single(XamlStyleRegistry.GetAll(uri));
        Assert.Equal(descriptor, restored);
    }

    [Fact]
    public void Template_Registry_Serializes_And_Restores_Descriptor()
    {
        const string uri = "avares://Demo/Templates.axaml";
        var descriptor = new SourceGenTemplateDescriptor(uri, "DataTemplate", "ItemTemplate", null, "global::Demo.ItemViewModel", "<DataTemplate />");
        XamlTemplateRegistry.Clear(uri);

        var payload = XamlTemplateRegistry.Serialize(descriptor);

        Assert.True(XamlTemplateRegistry.TryRegisterSerialized(payload));
        var restored = Assert.Single(XamlTemplateRegistry.GetAll(uri));
        Assert.Equal(descriptor, restored);
    }

    [Fact]
    public void Include_Registry_Serializes_And_Restores_Descriptor()
    {
        const string uri = "avares://Demo/App.axaml";
        var descriptor = new SourceGenIncludeDescriptor(uri, "StyleInclude", "/Styles/Common.axaml", "Styles", false, "<StyleInclude />");
        XamlIncludeRegistry.Clear(uri);

        var payload = XamlIncludeRegistry.Serialize(descriptor);

        Assert.True(XamlIncludeRegistry.TryRegisterSerialized(payload));
        var restored = Assert.Single(XamlIncludeRegistry.GetAll(uri));
        Assert.Equal(descriptor, restored);
    }

    [Fact]
    public void Registry_Rejects_Payload_With_Mismatched_Schema()
    {
        var payload = XamlResourceRegistry.Serialize(new SourceGenResourceDescriptor(
            "avares://Demo/Resources.axaml",
            "AccentBrush",
            "Brush",
            "<SolidColorBrush />"));

        Assert.False(XamlStyleRegistry.TryRegisterSerialized(payload));
        Assert.False(XamlTemplateRegistry.TryRegisterSerialized(payload));
        Assert.False(XamlIncludeRegistry.TryRegisterSerialized(payload));
    }

    [Fact]
    public void Registry_Rejects_Invalid_Or_Unsupported_Payloads()
    {
        const string invalidPayload = "{not-json}";
        const string unsupportedPayload = """
            {"schema":"axsg.runtime.registry.resource.v1","descriptor":[]}
            """;

        Assert.False(XamlResourceRegistry.TryRegisterSerialized(invalidPayload));
        Assert.False(XamlResourceRegistry.TryRegisterSerialized(unsupportedPayload));
    }

    [Fact]
    public void Descriptor_Register_Overloads_Validate_Null_Arguments()
    {
        Assert.Throws<ArgumentNullException>(() => XamlResourceRegistry.Register((SourceGenResourceDescriptor)null!));
        Assert.Throws<ArgumentNullException>(() => XamlStyleRegistry.Register((SourceGenStyleDescriptor)null!));
        Assert.Throws<ArgumentNullException>(() => XamlTemplateRegistry.Register((SourceGenTemplateDescriptor)null!));
        Assert.Throws<ArgumentNullException>(() => XamlIncludeRegistry.Register((SourceGenIncludeDescriptor)null!));
        Assert.Throws<ArgumentNullException>(() => XamlCompiledBindingRegistry.Register((SourceGenCompiledBindingDescriptor)null!));
        Assert.Throws<ArgumentNullException>(() => XamlControlThemeRegistry.Register((SourceGenControlThemeDescriptor)null!));
    }
}
