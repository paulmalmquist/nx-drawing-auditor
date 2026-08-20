using System.Globalization;
using System.Text;

namespace Relativity.DrawingAudit.Core;

public enum ValueComparisonStatus
{
    Match,
    Mismatch,
    Unsupported,
    Conflict
}

public sealed record ValueComparisonResult
{
    public ValueComparisonStatus Status { get; init; }

    public string? Observed { get; init; }

    public string? Expected { get; init; }

    public string? Difference { get; init; }

    public AuditDiagnostic? Diagnostic { get; init; }
}

public sealed record EffectiveAnnotationFacts
{
    public required DrawingAnnotation Annotation { get; init; }

    public required string SchemaVersion { get; init; }

    public string RawDisplayedText { get; init; } = string.Empty;

    public SemanticValueKind ValueKind { get; init; }

    public NumericComparisonMode? NumericComparisonMode { get; init; }

    public double? DisplayedNumericValue { get; init; }

    public string? DisplayedDesignation { get; init; }

    public double? NxAutomaticValue { get; init; }

    public string? NxAutomaticDesignation { get; init; }

    public double? CadValue { get; init; }

    public string? CadDesignation { get; init; }

    public string? Units { get; init; }

    public string? CadUnits { get; init; }

    public double? DisplayResolution { get; init; }

    public double ComparisonTolerance { get; init; }

    public ManualOverrideState ManualOverrideState { get; init; }

    public AssociationStatus AssociationStatus { get; init; }

    public ExtractionState ExtractionState { get; init; }

    public EvidenceConfidence Confidence { get; init; }

    public EvidenceAuthority Authority { get; init; }

    public string MeasurementProvenance { get; init; } = string.Empty;

    public IReadOnlyList<NxObjectReference> AssociatedGeometry { get; init; } = Array.Empty<NxObjectReference>();

    public NxObjectReference? DefinedFeature { get; init; }

    public DefinitionIdentity? DefinitionIdentity { get; init; }

    public string? PortFamily { get; init; }

    public string? MappingRevision { get; init; }

    public IReadOnlyList<string> CandidateCadDesignations { get; init; } = Array.Empty<string>();

    public bool DisplayedValueConflict { get; init; }

    public bool CadValueConflict { get; init; }

    public bool OverrideConflict { get; init; }

    public bool AssociationConflict { get; init; }

    public bool DefinitionConflict { get; init; }

    public bool AutomaticValueConflict { get; init; }

    public IReadOnlyList<AuditDiagnostic> Diagnostics { get; init; } = Array.Empty<AuditDiagnostic>();

    public bool IsSchema11 => SchemaVersion == "1.1";

    public bool HasAnyConflict =>
        DisplayedValueConflict ||
        CadValueConflict ||
        OverrideConflict ||
        AssociationConflict ||
        DefinitionConflict ||
        AutomaticValueConflict;

    public bool HasDirectDefinition =>
        DefinitionIdentity is { IsDirect: true } identity &&
        !string.IsNullOrWhiteSpace(identity.FeatureId) &&
        !string.IsNullOrWhiteSpace(identity.CharacteristicKey) &&
        identity.TargetSubgeometryIds is { Count: > 0 } targets &&
        targets.All(value => !string.IsNullOrWhiteSpace(value)) &&
        IsCompleteReference(DefinedFeature);

    public bool HasCompleteAnnotationReference => IsCompleteReference(Annotation.AnnotationObject);

    public bool HasCompleteAuthoritativeCadEvidence =>
        IsSchema11 &&
        ExtractionState == ExtractionState.Complete &&
        AssociationStatus == AssociationStatus.Associated &&
        Confidence == EvidenceConfidence.High &&
        Authority is EvidenceAuthority.DirectNx or EvidenceAuthority.ApprovedLocalMapping &&
        !string.IsNullOrWhiteSpace(MeasurementProvenance) &&
        HasCompleteAnnotationReference &&
        IsCompleteReference(DefinedFeature) &&
        AssociatedGeometry.Count != 0 &&
        AssociatedGeometry.All(IsCompleteReference) &&
        !DisplayedValueConflict &&
        !CadValueConflict &&
        !AssociationConflict &&
        !DefinitionConflict;

    public bool HasCompleteDirectDefinitionEvidence =>
        IsSchema11 &&
        ExtractionState == ExtractionState.Complete &&
        AssociationStatus == AssociationStatus.Associated &&
        Confidence == EvidenceConfidence.High &&
        HasCompleteAnnotationReference &&
        HasDirectDefinition &&
        AssociatedGeometry.Count != 0 &&
        AssociatedGeometry.All(IsCompleteReference) &&
        !AssociationConflict &&
        !DefinitionConflict;

    private static bool IsCompleteReference(NxObjectReference? reference) =>
        reference is not null &&
        !string.IsNullOrWhiteSpace(reference.EvidenceId) &&
        !string.IsNullOrWhiteSpace(reference.NxTag) &&
        !string.IsNullOrWhiteSpace(reference.ObjectType) &&
        !string.IsNullOrWhiteSpace(reference.OwningPart);
}

public static class EffectiveAnnotationFactsResolver
{
    public static EffectiveAnnotationFacts Resolve(AuditDocument document, DrawingAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotation);

        return document.SchemaVersion == "1.1"
            ? ResolveVersion11(document, annotation)
            : ResolveLegacy(document.SchemaVersion, annotation);
    }

    private static EffectiveAnnotationFacts ResolveLegacy(string schemaVersion, DrawingAnnotation annotation)
    {
        var unsupported = annotation.AssociationStatus == AssociationStatus.Unsupported;
        return new EffectiveAnnotationFacts
        {
            Annotation = annotation,
            SchemaVersion = schemaVersion,
            RawDisplayedText = annotation.DisplayedText,
            ValueKind = SemanticValueKind.SignedCoordinate,
            NumericComparisonMode = NumericComparisonMode.Signed,
            DisplayedNumericValue = annotation.DisplayedValue,
            CadValue = annotation.CadMeasuredValue,
            Units = null,
            CadUnits = null,
            ComparisonTolerance = annotation.ComparisonTolerance,
            ManualOverrideState = annotation.IsManualOverride
                ? ManualOverrideState.Overridden
                : ManualOverrideState.NotOverridden,
            AssociationStatus = unsupported ? AssociationStatus.Unknown : annotation.AssociationStatus,
            ExtractionState = unsupported ? ExtractionState.Unsupported : ExtractionState.Complete,
            Confidence = EvidenceConfidence.Unknown,
            Authority = EvidenceAuthority.Unknown,
            DefinitionIdentity = null,
            CandidateCadDesignations = Array.Empty<string>()
        };
    }

    private static EffectiveAnnotationFacts ResolveVersion11(
        AuditDocument document,
        DrawingAnnotation annotation)
    {
        var evidence = annotation.ValueEvidence;
        if (evidence is null)
        {
            var missing = Diagnostic(
                "VALUE_EVIDENCE_MISSING",
                "Schema 1.1 annotation is missing value evidence.",
                annotation);
            return new EffectiveAnnotationFacts
            {
                Annotation = annotation,
                SchemaVersion = "1.1",
                RawDisplayedText = annotation.DisplayedText,
                ValueKind = SemanticValueKind.Unknown,
                ComparisonTolerance = annotation.ComparisonTolerance,
                ManualOverrideState = ManualOverrideState.Unknown,
                AssociationStatus = AssociationStatus.Unknown,
                ExtractionState = ExtractionState.Incomplete,
                Confidence = EvidenceConfidence.Unknown,
                Authority = EvidenceAuthority.Unknown,
                DefinitionIdentity = annotation.DefinitionIdentity,
                Diagnostics = [missing]
            };
        }

        var diagnostics = new List<AuditDiagnostic>();
        if (evidence.Diagnostics is not null)
        {
            diagnostics.AddRange(evidence.Diagnostics.Select(item => item with
            {
                AnnotationId = item.AnnotationId ?? annotation.Id,
                EvidenceId = item.EvidenceId ?? annotation.AnnotationObject?.EvidenceId
            }));
        }

        var valueKind = evidence.ValueKind ?? SemanticValueKind.Unknown;
        var displayedValueConflict = HasDisplayedProjectionConflict(annotation, evidence, valueKind);
        if (displayedValueConflict)
        {
            diagnostics.Add(Diagnostic(
                "EVIDENCE_CONFLICT_DISPLAYED_VALUE",
                "The schema 1.1 displayed-value evidence disagrees with its legacy projection.",
                annotation));
        }

        var cadValueConflict = HasCadProjectionConflict(annotation, evidence, valueKind);
        if (cadValueConflict)
        {
            diagnostics.Add(Diagnostic(
                "EVIDENCE_CONFLICT_CAD_VALUE",
                "The schema 1.1 authoritative CAD evidence disagrees with its legacy projection.",
                annotation));
        }

        var overrideState = evidence.ManualOverrideState ?? ManualOverrideState.Unknown;
        var overrideConflict = overrideState switch
        {
            ManualOverrideState.Overridden => !annotation.IsManualOverride,
            ManualOverrideState.NotOverridden => annotation.IsManualOverride,
            _ => false
        };
        if (overrideConflict)
        {
            diagnostics.Add(Diagnostic(
                "EVIDENCE_CONFLICT_OVERRIDE_STATE",
                "The schema 1.1 manual-override evidence disagrees with its legacy projection.",
                annotation));
        }

        var association = evidence.AssociationStatus ?? AssociationStatus.Unknown;
        var associationConflict = association != AssociationStatus.Unknown &&
                                  annotation.AssociationStatus != association;
        if (associationConflict)
        {
            diagnostics.Add(Diagnostic(
                "EVIDENCE_CONFLICT_ASSOCIATION_STATE",
                "The schema 1.1 association evidence disagrees with its legacy projection.",
                annotation));
        }

        var definitionConflict = HasDefinitionProjectionConflict(document, annotation, evidence);
        if (definitionConflict)
        {
            diagnostics.Add(Diagnostic(
                "EVIDENCE_CONFLICT_DEFINITION_IDENTITY",
                "The schema 1.1 definition identity is missing, ambiguous, or disagrees with redundant feature evidence.",
                annotation));
        }

        var automaticConflict = HasAutomaticValueConflict(evidence, valueKind);
        if (automaticConflict)
        {
            diagnostics.Add(Diagnostic(
                "EVIDENCE_CONFLICT_AUTOMATIC_VALUE",
                "Displayed and NX automatic values disagree even though no manual override was reported.",
                annotation));
        }

        if (evidence.ManualOverrideState == ManualOverrideState.NotOverridden &&
            !HasAutomaticValue(evidence, valueKind))
        {
            automaticConflict = true;
            diagnostics.Add(Diagnostic(
                "NX_AUTOMATIC_VALUE_MISSING",
                "NX automatic value evidence is required to classify a non-overridden associative mismatch.",
                annotation));
        }

        return new EffectiveAnnotationFacts
        {
            Annotation = annotation,
            SchemaVersion = "1.1",
            RawDisplayedText = evidence.RawDisplayedText,
            ValueKind = valueKind,
            NumericComparisonMode = evidence.NumericComparisonMode,
            DisplayedNumericValue = evidence.ParsedNumericValue,
            DisplayedDesignation = evidence.ParsedDesignation,
            NxAutomaticValue = evidence.NxAutomaticValue,
            NxAutomaticDesignation = evidence.NxAutomaticDesignation,
            CadValue = evidence.CadValue,
            CadDesignation = evidence.CadDesignation,
            Units = evidence.Units,
            CadUnits = evidence.CadUnits,
            DisplayResolution = evidence.DisplayResolution,
            ComparisonTolerance = annotation.ComparisonTolerance,
            ManualOverrideState = overrideState,
            AssociationStatus = association == AssociationStatus.Unsupported ? AssociationStatus.Unknown : association,
            ExtractionState = evidence.AssociationStatus == AssociationStatus.Unsupported
                ? ExtractionState.Unsupported
                : evidence.ExtractionState ?? ExtractionState.Incomplete,
            Confidence = evidence.Confidence ?? EvidenceConfidence.Unknown,
            Authority = evidence.Authority ?? EvidenceAuthority.Unknown,
            MeasurementProvenance = evidence.MeasurementProvenance,
            AssociatedGeometry = evidence.AssociatedGeometry ?? Array.Empty<NxObjectReference>(),
            DefinedFeature = evidence.DefinedFeature,
            DefinitionIdentity = annotation.DefinitionIdentity,
            PortFamily = evidence.PortFamily,
            MappingRevision = evidence.MappingRevision,
            CandidateCadDesignations = evidence.CandidateCadDesignations ?? Array.Empty<string>(),
            DisplayedValueConflict = displayedValueConflict,
            CadValueConflict = cadValueConflict,
            OverrideConflict = overrideConflict,
            AssociationConflict = associationConflict,
            DefinitionConflict = definitionConflict,
            AutomaticValueConflict = automaticConflict,
            Diagnostics = diagnostics
        };
    }

    private static bool HasDisplayedProjectionConflict(
        DrawingAnnotation annotation,
        AnnotationValueEvidence evidence,
        SemanticValueKind valueKind)
    {
        if (!string.IsNullOrWhiteSpace(annotation.DisplayedText) &&
            !string.IsNullOrWhiteSpace(evidence.RawDisplayedText) &&
            !DisplayedTextEquivalent(annotation.DisplayedText, evidence.RawDisplayedText, valueKind))
        {
            return true;
        }

        if (valueKind == SemanticValueKind.DashDesignator)
        {
            if (annotation.DisplayedValue is not { } legacyValue || string.IsNullOrWhiteSpace(evidence.ParsedDesignation))
            {
                return false;
            }

            return AnnotationValueComparer.NormalizeDashDesignator(legacyValue.ToString("G17", CultureInfo.InvariantCulture)) !=
                   AnnotationValueComparer.NormalizeDashDesignator(evidence.ParsedDesignation);
        }

        return annotation.DisplayedValue is { } legacy && evidence.ParsedNumericValue is { } current &&
               !NearlyEqual(legacy, current);
    }

    private static bool HasCadProjectionConflict(
        DrawingAnnotation annotation,
        AnnotationValueEvidence evidence,
        SemanticValueKind valueKind)
    {
        if (annotation.CadMeasuredValue is not { } legacyValue)
        {
            return false;
        }

        if (valueKind == SemanticValueKind.DashDesignator)
        {
            return AnnotationValueComparer.NormalizeDashDesignator(legacyValue.ToString("G17", CultureInfo.InvariantCulture)) !=
                   AnnotationValueComparer.NormalizeDashDesignator(evidence.CadDesignation);
        }

        return evidence.CadValue is { } current && !NearlyEqual(legacyValue, current);
    }

    private static bool HasDefinitionProjectionConflict(
        AuditDocument document,
        DrawingAnnotation annotation,
        AnnotationValueEvidence evidence)
    {
        var identity = annotation.DefinitionIdentity;
        var definedFeature = evidence.DefinedFeature;
        if (definedFeature is not null)
        {
            var declaredFeature = document.Features.FirstOrDefault(feature =>
                string.Equals(feature.Id, definedFeature.EvidenceId, StringComparison.Ordinal) ||
                string.Equals(feature.ObjectReference?.EvidenceId, definedFeature.EvidenceId, StringComparison.Ordinal));
            if (declaredFeature is null ||
                !string.Equals(declaredFeature.NxTag, definedFeature.NxTag, StringComparison.Ordinal) ||
                !string.Equals(declaredFeature.OwningPart, definedFeature.OwningPart, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var geometry in evidence.AssociatedGeometry ?? Array.Empty<NxObjectReference>())
            {
                if (geometry.DirectOwnerEvidenceIds is null ||
                    !geometry.DirectOwnerEvidenceIds.Contains(definedFeature.EvidenceId, StringComparer.Ordinal) ||
                    !string.Equals(geometry.OwningPart, definedFeature.OwningPart, StringComparison.Ordinal) ||
                    geometry.OwningRevision is not null &&
                    definedFeature.OwningRevision is not null &&
                    !string.Equals(geometry.OwningRevision, definedFeature.OwningRevision, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        if (identity is null || string.IsNullOrWhiteSpace(identity.FeatureId))
        {
            return false;
        }

        if (annotation.AssociatedFeatureIds is { Count: > 0 })
        {
            var legacyContainsDefinedFeature = annotation.AssociatedFeatureIds
                .Any(value => string.Equals(value, identity.FeatureId, StringComparison.Ordinal));
            if (!legacyContainsDefinedFeature)
            {
                return true;
            }
        }

        if (definedFeature is not null &&
            !string.Equals(definedFeature.EvidenceId, identity.FeatureId, StringComparison.Ordinal))
        {
            return true;
        }

        if (identity.TargetSubgeometryIds is { Count: > 0 })
        {
            var geometryIds = (evidence.AssociatedGeometry ?? Array.Empty<NxObjectReference>())
                .Select(item => item.EvidenceId)
                .ToHashSet(StringComparer.Ordinal);
            if (identity.TargetSubgeometryIds.Any(target => !geometryIds.Contains(target)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAutomaticValueConflict(
        AnnotationValueEvidence evidence,
        SemanticValueKind valueKind)
    {
        if (evidence.ManualOverrideState != ManualOverrideState.NotOverridden)
        {
            return false;
        }

        return valueKind switch
        {
            SemanticValueKind.DashDesignator when !string.IsNullOrWhiteSpace(evidence.NxAutomaticDesignation) =>
                AnnotationValueComparer.NormalizeDashDesignator(evidence.ParsedDesignation ?? evidence.RawDisplayedText) !=
                AnnotationValueComparer.NormalizeDashDesignator(evidence.NxAutomaticDesignation),
            SemanticValueKind.NumericMagnitude or SemanticValueKind.SignedCoordinate
                when evidence.ParsedNumericValue is { } displayed && evidence.NxAutomaticValue is { } automatic =>
                !NumericValuesEquivalent(displayed, automatic, evidence.DisplayResolution, 0),
            _ => false
        };
    }

    private static bool HasAutomaticValue(AnnotationValueEvidence evidence, SemanticValueKind valueKind) =>
        valueKind == SemanticValueKind.DashDesignator
            ? !string.IsNullOrWhiteSpace(evidence.NxAutomaticDesignation)
            : evidence.NxAutomaticValue is not null;

    private static bool DisplayedTextEquivalent(string legacy, string current, SemanticValueKind valueKind)
    {
        if (valueKind == SemanticValueKind.DashDesignator)
        {
            return AnnotationValueComparer.NormalizeDashDesignator(legacy) ==
                   AnnotationValueComparer.NormalizeDashDesignator(current);
        }

        return double.TryParse(legacy, NumberStyles.Float, CultureInfo.InvariantCulture, out var legacyNumber) &&
               double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var currentNumber)
            ? NearlyEqual(legacyNumber, currentNumber)
            : string.Equals(legacy.Trim(), current.Trim(), StringComparison.Ordinal);
    }

    private static bool NearlyEqual(double first, double second)
    {
        var epsilon = Math.Max(1d, Math.Max(Math.Abs(first), Math.Abs(second))) * 1e-12;
        return Math.Abs(first - second) <= epsilon;
    }

    private static bool NumericValuesEquivalent(
        double first,
        double second,
        double? displayResolution,
        double profileTolerance)
    {
        if (displayResolution is not { } resolution || resolution <= 0)
        {
            return NearlyEqual(first, second);
        }

        var epsilon = Math.Max(1d, Math.Max(Math.Abs(first), Math.Abs(second))) * 1e-12;
        var threshold = Math.Max(profileTolerance, resolution / 2d + epsilon);
        return Math.Abs(first - second) <= threshold;
    }

    private static AuditDiagnostic Diagnostic(string code, string message, DrawingAnnotation annotation) =>
        new(code, FindingSeverity.Warning, message)
        {
            AnnotationId = annotation.Id,
            EvidenceId = annotation.AnnotationObject?.EvidenceId
        };
}

public static class AnnotationValueComparer
{
    private static readonly char[] DashCharacters =
    [
        '-',
        '\u2010',
        '\u2011',
        '\u2012',
        '\u2013',
        '\u2014',
        '\u2212',
        '\uFE63',
        '\uFF0D'
    ];

    public static ValueComparisonResult Compare(EffectiveAnnotationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.DisplayedValueConflict || facts.CadValueConflict)
        {
            return new ValueComparisonResult
            {
                Status = ValueComparisonStatus.Conflict,
                Diagnostic = Diagnostic(
                    "VALUE_COMPARISON_CONFLICT",
                    "Displayed or authoritative CAD value evidence conflicts with its legacy projection.",
                    facts)
            };
        }

        if (facts.ExtractionState is ExtractionState.Unsupported or ExtractionState.Failed or ExtractionState.Incomplete)
        {
            var evidenceDiagnostic = DiagnoseUnavailableEvidence(facts);
            return new ValueComparisonResult
            {
                Status = ValueComparisonStatus.Unsupported,
                Diagnostic = evidenceDiagnostic ?? Diagnostic(
                    facts.ExtractionState == ExtractionState.Failed ? "VALUE_EXTRACTION_FAILED" : "VALUE_EXTRACTION_INCOMPLETE",
                    $"Value comparison is unavailable because extraction state is {facts.ExtractionState}.",
                    facts)
            };
        }

        return facts.ValueKind switch
        {
            SemanticValueKind.DashDesignator => CompareDashDesignations(facts),
            SemanticValueKind.NumericMagnitude or SemanticValueKind.SignedCoordinate => CompareNumeric(facts),
            _ => new ValueComparisonResult
            {
                Status = ValueComparisonStatus.Unsupported,
                Diagnostic = Diagnostic("VALUE_KIND_UNSUPPORTED", "Value comparison requires a supported semantic value kind.", facts)
            }
        };
    }

    public static string? NormalizeDashDesignator(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            normalized.Append(DashCharacters.Contains(character) ? '-' : character);
        }

        var candidate = normalized.ToString();
        if (candidate.Length < 2 || candidate[0] != '-' || candidate[1..].Any(character => !char.IsAsciiDigit(character)))
        {
            return null;
        }

        var digits = candidate[1..].TrimStart('0');
        return $"-{(digits.Length == 0 ? "0" : digits)}";
    }

    internal static bool ValuesEquivalent(
        SemanticValueKind valueKind,
        NumericComparisonMode? comparisonMode,
        double? firstNumeric,
        double? secondNumeric,
        string? firstDesignation,
        string? secondDesignation,
        double? displayResolution,
        double profileTolerance)
    {
        if (valueKind == SemanticValueKind.DashDesignator)
        {
            var first = NormalizeDashDesignator(firstDesignation);
            var second = NormalizeDashDesignator(secondDesignation);
            return first is not null && first == second;
        }

        if (firstNumeric is not { } firstValue || secondNumeric is not { } secondValue)
        {
            return false;
        }

        if (comparisonMode == NumericComparisonMode.Magnitude)
        {
            firstValue = Math.Abs(firstValue);
            secondValue = Math.Abs(secondValue);
        }

        var epsilon = Math.Max(1d, Math.Max(Math.Abs(firstValue), Math.Abs(secondValue))) * 1e-12;
        var threshold = displayResolution is { } resolution && resolution > 0
            ? Math.Max(profileTolerance, resolution / 2d + epsilon)
            : Math.Max(profileTolerance, epsilon);
        return Math.Abs(firstValue - secondValue) <= threshold;
    }

    private static ValueComparisonResult CompareDashDesignations(EffectiveAnnotationFacts facts)
    {
        var canonicalCandidates = facts.CandidateCadDesignations
            .Select(NormalizeDashDesignator)
            .ToArray();
        if (canonicalCandidates.Any(candidate => candidate is null))
        {
            return Unsupported(
                "PORT_DESIGNATION_CANDIDATE_INVALID",
                "The approved port mapping contains a candidate that is not a canonical dash designation.",
                facts);
        }

        if (facts.Authority != EvidenceAuthority.ApprovedLocalMapping && canonicalCandidates.Length != 0)
        {
            return Unsupported(
                "PORT_MAPPING_CANDIDATES_UNEXPECTED",
                "Port-mapping candidates are present even though the CAD designation is classified as direct NX evidence.",
                facts);
        }

        if (facts.Authority == EvidenceAuthority.ApprovedLocalMapping)
        {
            if (string.IsNullOrWhiteSpace(facts.PortFamily) || string.IsNullOrWhiteSpace(facts.MappingRevision))
            {
                return Unsupported("PORT_MAPPING_PROVENANCE_MISSING", "Port-family and mapping-revision evidence are required for a mapped designation.", facts);
            }

            if (facts.CandidateCadDesignations.Count > 1)
            {
                return Unsupported("PORT_DESIGNATION_AMBIGUOUS", "The approved port mapping returned more than one possible designation.", facts);
            }
        }

        var displayed = NormalizeDashDesignator(facts.DisplayedDesignation ?? facts.RawDisplayedText);
        var cad = NormalizeDashDesignator(facts.CadDesignation);
        if (displayed is null)
        {
            return Unsupported("DISPLAYED_DESIGNATION_UNPARSEABLE", "Displayed port text is not a supported dash designation.", facts);
        }

        if (cad is null)
        {
            return Unsupported("CAD_DESIGNATION_UNAVAILABLE", "Authoritative CAD evidence does not contain one unambiguous dash designation.", facts);
        }

        if (canonicalCandidates.Length == 1 && canonicalCandidates[0] != cad)
        {
            return Unsupported(
                "PORT_MAPPING_RESULT_CONFLICT",
                "The approved mapping candidate disagrees with the authoritative CAD designation.",
                facts);
        }

        return new ValueComparisonResult
        {
            Status = displayed == cad ? ValueComparisonStatus.Match : ValueComparisonStatus.Mismatch,
            Observed = displayed,
            Expected = cad,
            Difference = null
        };
    }

    private static AuditDiagnostic? DiagnoseUnavailableEvidence(EffectiveAnnotationFacts facts)
    {
        if (facts.ValueKind == SemanticValueKind.DashDesignator)
        {
            if (facts.CandidateCadDesignations.Count > 1)
            {
                return Diagnostic("PORT_DESIGNATION_AMBIGUOUS", "The approved port mapping returned more than one possible designation.", facts);
            }

            if (facts.Authority == EvidenceAuthority.ApprovedLocalMapping &&
                (string.IsNullOrWhiteSpace(facts.PortFamily) || string.IsNullOrWhiteSpace(facts.MappingRevision)))
            {
                return Diagnostic("PORT_MAPPING_PROVENANCE_MISSING", "Port-family and mapping-revision evidence are required for a mapped designation.", facts);
            }

            if (NormalizeDashDesignator(facts.CadDesignation) is null)
            {
                return Diagnostic("CAD_DESIGNATION_UNAVAILABLE", "Authoritative CAD evidence does not contain one unambiguous dash designation.", facts);
            }
        }

        if (facts.ValueKind is SemanticValueKind.NumericMagnitude or SemanticValueKind.SignedCoordinate)
        {
            if (string.IsNullOrWhiteSpace(facts.Units) || string.IsNullOrWhiteSpace(facts.CadUnits))
            {
                return Diagnostic("NUMERIC_UNITS_UNAVAILABLE", "Displayed and CAD units are required for numeric comparison.", facts);
            }

            if (!string.Equals(facts.Units.Trim(), facts.CadUnits.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Diagnostic("NUMERIC_UNITS_INCOMPATIBLE", $"Displayed units '{facts.Units}' and CAD units '{facts.CadUnits}' are not normalized.", facts);
            }

            if (facts.DisplayResolution is not { } resolution || resolution <= 0 || !double.IsFinite(resolution))
            {
                return Diagnostic("DISPLAY_RESOLUTION_UNAVAILABLE", "Verified positive display resolution is required for numeric comparison.", facts);
            }
        }

        return null;
    }

    private static ValueComparisonResult CompareNumeric(EffectiveAnnotationFacts facts)
    {
        var expectedMode = facts.ValueKind == SemanticValueKind.NumericMagnitude
            ? NumericComparisonMode.Magnitude
            : NumericComparisonMode.Signed;
        if (facts.NumericComparisonMode != expectedMode)
        {
            return Unsupported("NUMERIC_COMPARISON_MODE_UNAVAILABLE", $"{facts.ValueKind} requires comparison mode {expectedMode}.", facts);
        }

        const NumberStyles supportedDisplayStyles =
            NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite |
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowDecimalPoint;
        if (!double.TryParse(
                facts.RawDisplayedText,
                supportedDisplayStyles,
                CultureInfo.InvariantCulture,
                out var rawDisplayedValue))
        {
            return Unsupported(
                "NUMERIC_DISPLAY_FORMAT_UNSUPPORTED",
                "Numeric comparison currently supports one verified decimal display only; fractional, scientific, dual-unit, decorated, or ambiguous text requires review.",
                facts);
        }

        if (facts.DisplayedNumericValue is not { } displayed || facts.CadValue is not { } cad)
        {
            return Unsupported("NUMERIC_VALUE_UNAVAILABLE", "Displayed and authoritative CAD numeric values are required.", facts);
        }

        var parseEpsilon = Math.Max(1d, Math.Max(Math.Abs(rawDisplayedValue), Math.Abs(displayed))) * 1e-12;
        if (Math.Abs(rawDisplayedValue - displayed) > parseEpsilon)
        {
            return new ValueComparisonResult
            {
                Status = ValueComparisonStatus.Conflict,
                Diagnostic = Diagnostic(
                    "DISPLAYED_NUMERIC_PARSE_CONFLICT",
                    "The parsed numeric value disagrees with the raw displayed decimal text.",
                    facts)
            };
        }

        if (string.IsNullOrWhiteSpace(facts.Units) || string.IsNullOrWhiteSpace(facts.CadUnits))
        {
            return Unsupported("NUMERIC_UNITS_UNAVAILABLE", "Displayed and CAD units are required for numeric comparison.", facts);
        }

        if (!string.Equals(facts.Units.Trim(), facts.CadUnits.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Unsupported("NUMERIC_UNITS_INCOMPATIBLE", $"Displayed units '{facts.Units}' and CAD units '{facts.CadUnits}' are not normalized.", facts);
        }

        if (facts.DisplayResolution is not { } resolution || resolution <= 0 || !double.IsFinite(resolution))
        {
            return Unsupported("DISPLAY_RESOLUTION_UNAVAILABLE", "Verified positive display resolution is required for numeric comparison.", facts);
        }

        if (!double.IsFinite(displayed) || !double.IsFinite(cad))
        {
            return Unsupported("NUMERIC_VALUE_NONFINITE", "Numeric comparison does not support non-finite values.", facts);
        }

        if (facts.NumericComparisonMode == NumericComparisonMode.Magnitude)
        {
            displayed = Math.Abs(displayed);
            cad = Math.Abs(cad);
        }

        var epsilon = Math.Max(1d, Math.Max(Math.Abs(displayed), Math.Abs(cad))) * 1e-12;
        var threshold = Math.Max(Math.Max(0, facts.ComparisonTolerance), resolution / 2d + epsilon);
        var difference = displayed - cad;
        return new ValueComparisonResult
        {
            Status = Math.Abs(difference) <= threshold ? ValueComparisonStatus.Match : ValueComparisonStatus.Mismatch,
            Observed = displayed.ToString("G17", CultureInfo.InvariantCulture),
            Expected = cad.ToString("G17", CultureInfo.InvariantCulture),
            Difference = difference.ToString("G17", CultureInfo.InvariantCulture)
        };
    }

    private static ValueComparisonResult Unsupported(string code, string message, EffectiveAnnotationFacts facts) =>
        new()
        {
            Status = ValueComparisonStatus.Unsupported,
            Diagnostic = Diagnostic(code, message, facts)
        };

    private static AuditDiagnostic Diagnostic(string code, string message, EffectiveAnnotationFacts facts) =>
        new(code, FindingSeverity.Warning, message)
        {
            AnnotationId = facts.Annotation.Id,
            EvidenceId = facts.Annotation.AnnotationObject?.EvidenceId,
            RuleId = "DRAWING_CAD_VALUE_MISMATCH"
        };
}
