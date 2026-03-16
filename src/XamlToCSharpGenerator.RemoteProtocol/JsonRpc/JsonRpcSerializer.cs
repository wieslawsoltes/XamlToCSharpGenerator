using System.Text.Json;

namespace XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

public static class JsonRpcSerializer
{
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
