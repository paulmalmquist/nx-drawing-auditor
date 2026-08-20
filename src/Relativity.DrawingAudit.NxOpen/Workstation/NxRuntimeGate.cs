using System.Text.Json;
using NXOpen;
using NXOpen.UF;

namespace Relativity.DrawingAudit.NxOpen;

internal sealed record NxRuntimeGate(
    string ManifestPath,
    string NxOpenDllPath,
    string NxOpenUfDllPath,
    string SessionMode)
{
    private const string ManifestEnvironmentVariable = "RELATIVITY_NX_AUDIT_MANIFEST";

    public static NxRuntimeGate Load(string? explicitManifestPath = null)
    {
        var manifestPath = explicitManifestPath;
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            manifestPath = Environment.GetEnvironmentVariable(ManifestEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new NxExtractionGateException(
                "NX_RUNTIME_GATE_001",
                $"Set {ManifestEnvironmentVariable} to the gated nx-environment.json path before running the journal.");
        }

        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath))
        {
            throw new NxExtractionGateException(
                "NX_RUNTIME_GATE_002",
                "The gated NX environment manifest does not exist.",
                new Dictionary<string, string?> { ["manifestPath"] = fullManifestPath });
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(fullManifestPath));
            var root = document.RootElement;
            if (!ReadRequiredBoolean(root, "gateReady"))
            {
                throw new NxExtractionGateException(
                    "NX_RUNTIME_GATE_003",
                    "The NX environment manifest is not gate-ready. Resolve its blockers and regenerate it.",
                    new Dictionary<string, string?> { ["manifestPath"] = fullManifestPath });
            }

            var installation = ReadRequiredObject(root, "selectedInstallation");
            var nxOpen = ReadRequiredObject(installation, "nxOpen");
            var nxOpenUf = ReadRequiredObject(installation, "nxOpenUf");
            var session = ReadRequiredObject(root, "session");
            var sessionMode = ReadRequiredString(session, "mode");
            if (!string.Equals(sessionMode, "native", StringComparison.OrdinalIgnoreCase))
            {
                throw new NxExtractionGateException(
                    "NX_RUNTIME_GATE_004",
                    "Only an operator-confirmed native NX session is permitted for this milestone.",
                    new Dictionary<string, string?> { ["sessionMode"] = sessionMode });
            }

            return new NxRuntimeGate(
                fullManifestPath,
                Path.GetFullPath(ReadRequiredString(nxOpen, "path")),
                Path.GetFullPath(ReadRequiredString(nxOpenUf, "path")),
                sessionMode);
        }
        catch (NxExtractionGateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new NxExtractionGateException(
                "NX_RUNTIME_GATE_005",
                "The NX environment manifest could not be read or was malformed.",
                new Dictionary<string, string?> { ["manifestPath"] = fullManifestPath },
                exception);
        }
    }

    public void VerifyLoadedAssemblies()
    {
        VerifyLoadedAssembly("NXOpen", NxOpenDllPath, typeof(Session).Assembly.Location);
        VerifyLoadedAssembly("NXOpen.UF", NxOpenUfDllPath, typeof(UFSession).Assembly.Location);
    }

    private static void VerifyLoadedAssembly(string name, string expectedPath, string actualPath)
    {
        var expected = Path.GetFullPath(expectedPath);
        var actual = Path.GetFullPath(actualPath);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new NxExtractionGateException(
                "NX_RUNTIME_GATE_006",
                $"Loaded {name} does not match the exact assembly approved by the workstation manifest.",
                new Dictionary<string, string?>
                {
                    ["expectedPath"] = expected,
                    ["actualPath"] = actual
                });
        }
    }

    private static JsonElement ReadRequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new NxExtractionGateException(
                "NX_RUNTIME_GATE_007",
                $"The NX environment manifest is missing object '{name}'.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new NxExtractionGateException(
                "NX_RUNTIME_GATE_008",
                $"The NX environment manifest is missing string '{name}'.");
        }

        return value.GetString()!;
    }

    private static bool ReadRequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            throw new NxExtractionGateException(
                "NX_RUNTIME_GATE_009",
                $"The NX environment manifest is missing Boolean '{name}'.");
        }

        return value.GetBoolean();
    }
}
