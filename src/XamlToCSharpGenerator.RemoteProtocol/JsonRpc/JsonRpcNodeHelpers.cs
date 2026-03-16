using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

public static class JsonRpcNodeHelpers
{
    public static JsonNode? CloneJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => JsonValue.Create(element.GetString()),
            JsonValueKind.Number => CloneNumberElement(element),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.Object => CloneObjectElement(element),
            JsonValueKind.Array => CloneArrayElement(element),
            _ => JsonValue.Create(element.GetRawText())
        };
    }

    public static JsonNode? SerializeResultValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonNode node)
        {
            return JsonNode.Parse(node.ToJsonString());
        }

        if (value is JsonElement element)
        {
            return CloneJsonElement(element);
        }

        return JsonSerializer.SerializeToNode(value, JsonRpcSerializer.DefaultOptions);
    }

    private static JsonNode CloneNumberElement(JsonElement element)
    {
        if (element.TryGetInt64(out var int64Value))
        {
            return JsonValue.Create(int64Value)!;
        }

        if (element.TryGetUInt64(out var uint64Value))
        {
            return JsonValue.Create(uint64Value)!;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            return JsonValue.Create(decimalValue)!;
        }

        if (element.TryGetDouble(out var doubleValue))
        {
            return JsonValue.Create(doubleValue)!;
        }

        var rawNumber = element.GetRawText();
        if (decimal.TryParse(rawNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDecimal))
        {
            return JsonValue.Create(parsedDecimal)!;
        }

        if (double.TryParse(rawNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
        {
            return JsonValue.Create(parsedDouble)!;
        }

        return JsonValue.Create(rawNumber)!;
    }

    private static JsonObject CloneObjectElement(JsonElement element)
    {
        var objectNode = new JsonObject();
        foreach (var property in element.EnumerateObject())
        {
            objectNode[property.Name] = CloneJsonElement(property.Value);
        }

        return objectNode;
    }

    private static JsonArray CloneArrayElement(JsonElement element)
    {
        var arrayNode = new JsonArray();
        foreach (var item in element.EnumerateArray())
        {
            arrayNode.Add(CloneJsonElement(item));
        }

        return arrayNode;
    }
}
