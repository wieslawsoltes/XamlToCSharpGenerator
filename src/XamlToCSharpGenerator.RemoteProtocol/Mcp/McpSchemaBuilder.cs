using System.Linq;
using System.Text.Json.Nodes;

namespace XamlToCSharpGenerator.RemoteProtocol.Mcp;

/// <summary>
/// Provides helpers for building MCP JSON Schema payloads.
/// </summary>
public static class McpSchemaBuilder
{
    /// <summary>
    /// Builds an object schema with no required properties.
    /// </summary>
    public static JsonObject BuildObjectSchema(params (string Name, JsonNode Schema)[] properties)
    {
        return BuildObjectSchema(null, properties);
    }

    /// <summary>
    /// Builds an object schema with optional required properties.
    /// </summary>
    public static JsonObject BuildObjectSchema(string[]? required, params (string Name, JsonNode Schema)[] properties)
    {
        var propertyObject = new JsonObject();
        for (int index = 0; index < properties.Length; index++)
        {
            propertyObject[properties[index].Name] = properties[index].Schema;
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = propertyObject,
            ["additionalProperties"] = false
        };

        if (required is not null && required.Length > 0)
        {
            schema["required"] = new JsonArray(required.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray());
        }

        return schema;
    }

    /// <summary>
    /// Builds a string schema with a description.
    /// </summary>
    public static JsonObject BuildStringSchema(string description)
    {
        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description
        };
    }

    /// <summary>
    /// Builds a number schema with a description.
    /// </summary>
    public static JsonObject BuildNumberSchema(string description)
    {
        return new JsonObject
        {
            ["type"] = "number",
            ["description"] = description
        };
    }

    /// <summary>
    /// Builds an integer schema with a description.
    /// </summary>
    public static JsonObject BuildIntegerSchema(string description)
    {
        return new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description
        };
    }

    /// <summary>
    /// Builds a boolean schema with a description.
    /// </summary>
    public static JsonObject BuildBooleanSchema(string description)
    {
        return new JsonObject
        {
            ["type"] = "boolean",
            ["description"] = description
        };
    }
}
