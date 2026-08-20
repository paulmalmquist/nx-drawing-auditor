using System.Text.Json;
using NXOpen;

namespace Relativity.DrawingAudit.NxOpen;

/// <summary>
/// Candidate compiled-journal entry point. Its signature must be checked against the
/// installed NX template and recorded journal before the workstation gate is marked ready.
/// </summary>
public static class NxJournalEntryPoint
{
    public static void Main(string[] args)
    {
        var manifestPath = args.Length > 0 ? args[0] : null;
        try
        {
            var extractor = new NxOpenExtractor(manifestPath);
            _ = extractor.ExtractCurrentDrawingAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            WriteOperationalDiagnostic(exception);
            throw;
        }
    }

    public static int GetUnloadOption(string unused) =>
        (int)Session.LibraryUnloadOption.Immediately;

    private static void WriteOperationalDiagnostic(Exception exception)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            return;
        }

        var runId = $"{DateTime.UtcNow:yyyyMMdd'T'HHmmss.fff'Z'}-{Guid.NewGuid():N}";
        var runDirectory = Path.Combine(localData, "Relativity.DrawingAudit", "runs", runId);
        Directory.CreateDirectory(runDirectory);

        var gateException = exception as NxExtractionGateException;
        var diagnostic = new
        {
            schemaVersion = "1.0",
            status = "incomplete",
            sourceSystem = "Siemens NX",
            generatedUtc = DateTime.UtcNow,
            auditInputWritten = false,
            processingBoundary = "local-only",
            diagnostic = new
            {
                code = gateException?.DiagnosticCode ?? "NX_EXTRACTION_UNEXPECTED_FAILURE",
                message = exception.Message,
                exceptionType = exception.GetType().FullName,
                details = gateException?.Details
            }
        };

        var finalPath = Path.Combine(runDirectory, "extraction-diagnostic.json");
        var temporaryPath = $"{finalPath}.{Environment.ProcessId}.tmp";
        var json = JsonSerializer.Serialize(diagnostic, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, finalPath, true);
    }
}
