using System;
using System.Text.Json;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenRegistryPayloadSchemas
{
    public const string ResourceV1 = "axsg.runtime.registry.resource.v1";
    public const string StyleV1 = "axsg.runtime.registry.style.v1";
    public const string TemplateV1 = "axsg.runtime.registry.template.v1";
    public const string IncludeV1 = "axsg.runtime.registry.include.v1";
}

public static class SourceGenRegistryPayloadSerialization
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(SourceGenResourceDescriptor descriptor)
    {
        return Serialize(SourceGenRegistryPayloadSchemas.ResourceV1, descriptor);
    }

    public static string Serialize(SourceGenStyleDescriptor descriptor)
    {
        return Serialize(SourceGenRegistryPayloadSchemas.StyleV1, descriptor);
    }

    public static string Serialize(SourceGenTemplateDescriptor descriptor)
    {
        return Serialize(SourceGenRegistryPayloadSchemas.TemplateV1, descriptor);
    }

    public static string Serialize(SourceGenIncludeDescriptor descriptor)
    {
        return Serialize(SourceGenRegistryPayloadSchemas.IncludeV1, descriptor);
    }

    public static bool TryDeserializeResource(
        string payload,
        out SourceGenResourceDescriptor? descriptor)
    {
        return TryDeserialize(payload, SourceGenRegistryPayloadSchemas.ResourceV1, out descriptor);
    }

    public static bool TryDeserializeStyle(
        string payload,
        out SourceGenStyleDescriptor? descriptor)
    {
        return TryDeserialize(payload, SourceGenRegistryPayloadSchemas.StyleV1, out descriptor);
    }

    public static bool TryDeserializeTemplate(
        string payload,
        out SourceGenTemplateDescriptor? descriptor)
    {
        return TryDeserialize(payload, SourceGenRegistryPayloadSchemas.TemplateV1, out descriptor);
    }

    public static bool TryDeserializeInclude(
        string payload,
        out SourceGenIncludeDescriptor? descriptor)
    {
        return TryDeserialize(payload, SourceGenRegistryPayloadSchemas.IncludeV1, out descriptor);
    }

    private static string Serialize<TDescriptor>(
        string schema,
        TDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        var payload = new RegistryPayloadEnvelope<TDescriptor>(schema, descriptor);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static bool TryDeserialize<TDescriptor>(
        string payload,
        string expectedSchema,
        out TDescriptor? descriptor)
    {
        descriptor = default;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        RegistryPayloadEnvelope<TDescriptor>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<RegistryPayloadEnvelope<TDescriptor>>(payload, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }

        if (envelope is null ||
            !string.Equals(envelope.Schema, expectedSchema, StringComparison.Ordinal) ||
            envelope.Descriptor is null)
        {
            return false;
        }

        descriptor = envelope.Descriptor;
        return true;
    }

    private sealed record RegistryPayloadEnvelope<TDescriptor>(
        string Schema,
        TDescriptor Descriptor);
}
