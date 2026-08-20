using NXOpen;
using Relativity.DrawingAudit.Core;

namespace Relativity.DrawingAudit.NxOpen;

/// <summary>
/// Workstation-only NX Open boundary. This source is compiled only when the
/// manifest-driven NX gate has passed and installed Siemens assemblies are supplied.
/// </summary>
public sealed class NxOpenExtractor : IDrawingExtractor
{
    private readonly string? manifestPath;

    public NxOpenExtractor(string? manifestPath = null)
    {
        this.manifestPath = manifestPath;
    }

    public string SourceSystem => "Siemens NX";

    public Task<AuditDocument> ExtractCurrentDrawingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var gate = NxRuntimeGate.Load(manifestPath);
        gate.VerifyLoadedAssemblies();

        // Session.GetSession and Parts.Display are read-only accessors. No part-open,
        // update, builder, commit, save, close, revision, or Teamcenter API is called.
        var session = Session.GetSession();
        if (session.Parts.Display is null)
        {
            throw new NxExtractionGateException(
                "NX_EXTRACTION_001",
                "NX has no active displayed part. Open the native drawing and load its referenced model before extracting.");
        }

        // Deliberately stop here until the workstation journal has established the
        // exact release-specific APIs for modified flags, sheets, annotations,
        // associativity, feature ownership, and port designation provenance. Returning
        // a partial AuditDocument here would make incomplete evidence look authoritative.
        throw new NxExtractionGateException(
            "NX_API_REVIEW_REQUIRED",
            "The installed-release journal/API mapping has not yet been approved. No audit input was produced.",
            new Dictionary<string, string?>
            {
                ["manifestPath"] = gate.ManifestPath,
                ["sessionMode"] = gate.SessionMode,
                ["requiredNextStep"] = "Review the recorded journal and implement only verified read-only NX API mappings."
            });
    }
}
