using System.Text.Json.Serialization;

namespace Relativity.DrawingAudit.Core;

public enum AnnotationKind
{
    Dimension,
    HoleCallout,
    FeatureControlFrame,
    Datum,
    Note
}

public enum AssociationStatus
{
    Associated,
    PartiallyAssociated,
    Broken,
    Unsupported,
    Unknown
}

public enum ExtractionState
{
    Complete,
    Incomplete,
    Unsupported,
    Failed
}

public enum EvidenceConfidence
{
    Unknown,
    Low,
    Medium,
    High
}

public enum SemanticValueKind
{
    Unknown,
    DashDesignator,
    NumericMagnitude,
    SignedCoordinate
}

public enum NumericComparisonMode
{
    Magnitude,
    Signed
}

public enum ManualOverrideState
{
    Unknown,
    NotOverridden,
    Overridden
}

public enum EvidenceAuthority
{
    Unknown,
    DirectNx,
    ApprovedLocalMapping
}

public enum AnnotationAssessment
{
    Unknown,
    Consistent,
    ManualOverride,
    AssociativeButStale,
    PartiallyAssociated,
    BrokenAssociation,
    Unsupported,
    ExtractionFailed,
    EvidenceConflict
}

public enum RuleApprovalStatus
{
    Pending,
    Approved,
    Retired
}

public enum GdtCharacteristic
{
    None,
    Straightness,
    Flatness,
    Circularity,
    Cylindricity,
    ProfileOfLine,
    ProfileOfSurface,
    Angularity,
    Perpendicularity,
    Parallelism,
    Position,
    Concentricity,
    Symmetry,
    CircularRunout,
    TotalRunout
}

public enum FindingSeverity
{
    Information,
    Warning,
    Error
}

public sealed record DrawingIdentity(
    [property: JsonRequired] string PartNumber,
    [property: JsonRequired] string Revision,
    [property: JsonRequired] string Units,
    [property: JsonRequired] string StandardProfile);

public sealed record AuditDocument(
    [property: JsonRequired] string SchemaVersion,
    [property: JsonRequired] DrawingIdentity Drawing,
    [property: JsonRequired] IReadOnlyList<DrawingSheet> Sheets,
    [property: JsonRequired] IReadOnlyList<CadFeature> Features)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExtractionMetadata? ExtractionMetadata { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AuditDiagnostic>? Diagnostics { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RuleMetadata>? RuleMetadata { get; init; }
}

public sealed record DrawingSheet(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] IReadOnlyList<DrawingView> Views);

public sealed record DrawingView(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string ReferencedPart,
    [property: JsonRequired] double Scale,
    [property: JsonRequired] IReadOnlyList<DrawingAnnotation> Annotations)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EvidenceId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Orientation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DrawingViewTransform? Transform { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NxObjectReference? ReferencedPartIdentity { get; init; }
}

public sealed record DrawingAnnotation(
    [property: JsonRequired] string Id,
    [property: JsonRequired] AnnotationKind Kind,
    [property: JsonRequired] string DisplayedText,
    [property: JsonRequired] double? DisplayedValue,
    [property: JsonRequired] double? CadMeasuredValue,
    [property: JsonRequired] double ComparisonTolerance,
    [property: JsonRequired] bool IsReference,
    [property: JsonRequired] bool IsManualOverride,
    [property: JsonRequired] AssociationStatus AssociationStatus,
    [property: JsonRequired] IReadOnlyList<string> AssociatedFeatureIds,
    [property: JsonRequired] GdtFrame? Gdt,
    [property: JsonRequired] EvidenceLocation? Location)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnnotationValueEvidence? ValueEvidence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DefinitionIdentity? DefinitionIdentity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NxObjectReference? AnnotationObject { get; init; }
}

public sealed record GdtFrame(
    [property: JsonRequired] GdtCharacteristic Characteristic,
    [property: JsonRequired] double? Tolerance,
    [property: JsonRequired] IReadOnlyList<string> DatumReferences,
    [property: JsonRequired] IReadOnlyList<string> Modifiers);

public sealed record CadFeature(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string NxTag,
    [property: JsonRequired] string FeatureType,
    [property: JsonRequired] string OwningPart)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NxObjectReference? ObjectReference { get; init; }
}

public sealed record EvidenceLocation(
    [property: JsonRequired] string SheetId,
    [property: JsonRequired] string ViewId,
    [property: JsonRequired] double? X,
    [property: JsonRequired] double? Y);

public sealed record AuditFinding(
    string RuleId,
    FindingSeverity Severity,
    string Message,
    string AnnotationId,
    string? FeatureId,
    string? Observed,
    string? Expected,
    EvidenceLocation? Location)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Difference { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnnotationAssessment? Assessment { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EvidenceConfidence? Confidence { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EvidenceId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuleMetadata? Metadata { get; init; }
}

public sealed record AuditResult(
    AuditDocument Document,
    IReadOnlyList<AuditFinding> Findings)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AuditDiagnostic>? Diagnostics { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RuleMetadata>? RuleMetadata { get; init; }
}

public sealed record ExtractionMetadata
{
    [JsonRequired]
    public string SourceSystem { get; init; } = string.Empty;

    [JsonRequired]
    public string ExtractorVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string RunId { get; init; } = string.Empty;

    [JsonRequired]
    public DateTimeOffset ExtractedAtUtc { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NxRelease { get; init; }

    [JsonRequired]
    public bool DrawingModifiedBefore { get; init; }

    [JsonRequired]
    public bool DrawingModifiedAfter { get; init; }

    [JsonRequired]
    public bool ReferencedModelModifiedBefore { get; init; }

    [JsonRequired]
    public bool ReferencedModelModifiedAfter { get; init; }
}

public sealed record DrawingViewTransform
{
    [JsonRequired]
    public IReadOnlyList<double> Matrix { get; init; } = Array.Empty<double>();

    [JsonRequired]
    public IReadOnlyList<double> Translation { get; init; } = Array.Empty<double>();
}

public sealed record NxObjectReference
{
    [JsonRequired]
    public string EvidenceId { get; init; } = string.Empty;

    [JsonRequired]
    public string NxTag { get; init; } = string.Empty;

    [JsonRequired]
    public string ObjectType { get; init; } = string.Empty;

    [JsonRequired]
    public string OwningPart { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwningRevision { get; init; }

    [JsonRequired]
    public IReadOnlyList<string> DirectOwnerEvidenceIds { get; init; } = Array.Empty<string>();
}

public sealed record DefinitionIdentity
{
    [JsonRequired]
    public string FeatureId { get; init; } = string.Empty;

    [JsonRequired]
    public string CharacteristicKey { get; init; } = string.Empty;

    [JsonRequired]
    public IReadOnlyList<string> TargetSubgeometryIds { get; init; } = Array.Empty<string>();

    [JsonRequired]
    public bool IsDirect { get; init; }
}

public sealed record AnnotationValueEvidence
{
    [JsonRequired]
    public string RawDisplayedText { get; init; } = string.Empty;

    [JsonRequired]
    public SemanticValueKind? ValueKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NumericComparisonMode? NumericComparisonMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ParsedNumericValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParsedDesignation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Units { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CadUnits { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DisplayResolution { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? NxAutomaticValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NxAutomaticDesignation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? CadValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CadDesignation { get; init; }

    [JsonRequired]
    public string MeasurementProvenance { get; init; } = string.Empty;

    [JsonRequired]
    public ManualOverrideState? ManualOverrideState { get; init; }

    [JsonRequired]
    public AssociationStatus? AssociationStatus { get; init; }

    [JsonRequired]
    public ExtractionState? ExtractionState { get; init; }

    [JsonRequired]
    public EvidenceConfidence? Confidence { get; init; }

    [JsonRequired]
    public EvidenceAuthority? Authority { get; init; }

    [JsonRequired]
    public IReadOnlyList<NxObjectReference> AssociatedGeometry { get; init; } = Array.Empty<NxObjectReference>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NxObjectReference? DefinedFeature { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PortFamily { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MappingRevision { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? CandidateCadDesignations { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AuditDiagnostic>? Diagnostics { get; init; }
}

public sealed record AuditDiagnostic(
    [property: JsonRequired] string Code,
    [property: JsonRequired] FindingSeverity Severity,
    [property: JsonRequired] string Message)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AnnotationId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EvidenceId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleId { get; init; }
}

public sealed record RuleMetadata
{
    [JsonRequired]
    public string RuleId { get; init; } = string.Empty;

    [JsonRequired]
    public string StandardName { get; init; } = string.Empty;

    [JsonRequired]
    public string Edition { get; init; } = string.Empty;

    [JsonRequired]
    public string ParagraphCitation { get; init; } = string.Empty;

    [JsonRequired]
    public string Interpretation { get; init; } = string.Empty;

    [JsonRequired]
    public string Applicability { get; init; } = string.Empty;

    [JsonRequired]
    public string PermittedExceptions { get; init; } = string.Empty;

    [JsonRequired]
    public FindingSeverity Severity { get; init; }

    [JsonRequired]
    public string EngineeringOwner { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? ApprovalDate { get; init; }

    [JsonRequired]
    public RuleApprovalStatus ApprovalStatus { get; init; } = RuleApprovalStatus.Pending;

    [JsonRequired]
    public IReadOnlyList<string> EvidenceRequirements { get; init; } = Array.Empty<string>();

    [JsonRequired]
    public string IncompleteEvidenceBehavior { get; init; } = string.Empty;
}
