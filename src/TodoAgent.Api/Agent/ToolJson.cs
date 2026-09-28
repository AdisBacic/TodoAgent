using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoAgent.Api.Agent;

/// <summary>JSON conventions shared by every tool: how inputs are read and results written.</summary>
public static class ToolJson
{
    /// <summary>
    /// camelCase like the HTTP API, plus lenient numbers: models occasionally
    /// send <c>"id": "3"</c> and there is no reason to fail them for it.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Parses a hand-written JSON Schema once, at type initialisation.</summary>
    public static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
