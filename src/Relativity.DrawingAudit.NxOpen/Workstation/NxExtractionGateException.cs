namespace Relativity.DrawingAudit.NxOpen;

/// <summary>
/// A fail-closed extraction outcome. These exceptions describe an operational blocker;
/// they are not engineering findings and must never be converted into guessed evidence.
/// </summary>
public sealed class NxExtractionGateException : InvalidOperationException
{
    public NxExtractionGateException(
        string diagnosticCode,
        string message,
        IReadOnlyDictionary<string, string?>? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DiagnosticCode = diagnosticCode;
        Details = details ?? new Dictionary<string, string?>();
    }

    public string DiagnosticCode { get; }

    public IReadOnlyDictionary<string, string?> Details { get; }
}
