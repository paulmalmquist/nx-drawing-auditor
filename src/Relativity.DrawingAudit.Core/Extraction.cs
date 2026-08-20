namespace Relativity.DrawingAudit.Core;

public interface IDrawingExtractor
{
    string SourceSystem { get; }

    Task<AuditDocument> ExtractCurrentDrawingAsync(CancellationToken cancellationToken = default);
}
