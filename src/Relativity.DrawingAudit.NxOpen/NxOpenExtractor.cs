using Relativity.DrawingAudit.Core;

namespace Relativity.DrawingAudit.NxOpen;

/// <summary>
/// Portable NX integration boundary. Enabling the workstation implementation requires
/// the gated build described by scripts/Find-NxOpen.ps1 and
/// scripts/Build-NxOpenAdapter.ps1.
/// </summary>
public sealed class NxOpenExtractor : IDrawingExtractor
{
    public string SourceSystem => "Siemens NX";

    public Task<AuditDocument> ExtractCurrentDrawingAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "NXOPEN_NOT_ENABLED: This is the portable, no-NX adapter. Create and pass the " +
            "workstation gate before building with EnableNxOpen=true. No drawing data was read.");
}
