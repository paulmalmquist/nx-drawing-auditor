using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relativity.DrawingAudit.Core;

public static class AuditJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    internal static JsonSerializerOptions Version11Options { get; } = CreateVersion11Options();

    public static AuditDocument DeserializeDocument(string json) =>
        AuditDocumentLoader.Load(json);

    public static AuditDocument DeserializeDocument(Stream utf8Json) =>
        AuditDocumentLoader.Load(utf8Json);

    public static Task<AuditDocument> DeserializeDocumentAsync(
        Stream utf8Json,
        CancellationToken cancellationToken = default) =>
        AuditDocumentLoader.LoadAsync(utf8Json, cancellationToken);

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static JsonSerializerOptions CreateVersion11Options() =>
        new(Options)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
}
