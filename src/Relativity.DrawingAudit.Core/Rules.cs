using System.Globalization;

namespace Relativity.DrawingAudit.Core;

public interface IAuditRule
{
    string Id { get; }

    RuleMetadata Metadata => RuleCatalog.Get(Id);

    IEnumerable<AuditFinding> Evaluate(AuditDocument document);
}

public sealed class AuditEngine(IEnumerable<IAuditRule> rules)
{
    private readonly IReadOnlyList<IAuditRule> _rules = rules.ToArray();

    public static AuditEngine CreateDefault() => new(
    [
        new DrawingCadValueMismatchRule(),
        new ManualDimensionOverrideRule(),
        new BrokenDimensionAssociativityRule(),
        new DuplicateFeatureDefinitionRule(),
        new FlatnessReferencesDatumRule()
    ]);

    public AuditResult Run(AuditDocument document)
    {
        AuditDocumentValidator.ValidateAndThrow(document);

        var findings = _rules.SelectMany(rule => rule.Evaluate(document)).ToArray();
        if (document.SchemaVersion == "1.0")
        {
            return new AuditResult(document, findings);
        }

        var enrichedFindings = findings
            .Select(finding => finding with
            {
                Metadata = RuleCatalog.Get(finding.RuleId) with { Severity = finding.Severity }
            })
            .ToArray();
        var diagnostics = AuditDiagnostics.Collect(document);
        return new AuditResult(document, enrichedFindings)
        {
            Diagnostics = diagnostics.Count == 0 ? null : diagnostics,
            RuleMetadata = RuleCatalog.All
        };
    }
}

internal static class AuditTraversal
{
    public static IEnumerable<(DrawingSheet Sheet, DrawingView View, DrawingAnnotation Annotation)> Annotations(AuditDocument document) =>
        document.Sheets.SelectMany(sheet => sheet.Views
            .SelectMany(view => view.Annotations.Select(annotation => (sheet, view, annotation))));
}

public static class AuditDiagnostics
{
    public static IReadOnlyList<AuditDiagnostic> Collect(AuditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<AuditDiagnostic>();
        if (document.Diagnostics is not null)
        {
            diagnostics.AddRange(document.Diagnostics);
        }

        if (document.SchemaVersion != "1.1")
        {
            return Deduplicate(diagnostics);
        }

        foreach (var (_, _, annotation) in AuditTraversal.Annotations(document))
        {
            if (annotation.Kind is not (AnnotationKind.Dimension or AnnotationKind.HoleCallout))
            {
                if (annotation.Kind == AnnotationKind.FeatureControlFrame)
                {
                    diagnostics.Add(new AuditDiagnostic(
                        "GDT_EXTRACTION_EVIDENCE_UNSUPPORTED",
                        FindingSeverity.Warning,
                        "Schema 1.1 does not yet carry verified feature-control-frame parse state, confidence, and provenance; the flatness conclusion was suppressed.")
                    {
                        AnnotationId = annotation.Id,
                        EvidenceId = annotation.AnnotationObject?.EvidenceId,
                        RuleId = "FLATNESS_REFERENCES_DATUM"
                    });
                }

                continue;
            }

            var facts = EffectiveAnnotationFactsResolver.Resolve(document, annotation);
            diagnostics.AddRange(facts.Diagnostics);

            var comparison = AnnotationValueComparer.Compare(facts);
            if (comparison.Diagnostic is not null)
            {
                diagnostics.Add(comparison.Diagnostic);
            }

            if (facts.ExtractionState is ExtractionState.Unsupported or ExtractionState.Failed)
            {
                diagnostics.Add(new AuditDiagnostic(
                    facts.ExtractionState == ExtractionState.Failed ? "ANNOTATION_EXTRACTION_FAILED" : "ANNOTATION_EXTRACTION_UNSUPPORTED",
                    FindingSeverity.Warning,
                    $"Annotation extraction state is {facts.ExtractionState}; no engineering conclusion was made.")
                {
                    AnnotationId = annotation.Id,
                    EvidenceId = annotation.AnnotationObject?.EvidenceId
                });
            }
            else if (facts.ExtractionState == ExtractionState.Incomplete)
            {
                diagnostics.Add(new AuditDiagnostic(
                    "ANNOTATION_EXTRACTION_INCOMPLETE",
                    FindingSeverity.Warning,
                    "Annotation extraction is incomplete; evidence-dependent engineering conclusions were suppressed.")
                {
                    AnnotationId = annotation.Id,
                    EvidenceId = annotation.AnnotationObject?.EvidenceId
                });
            }

            if (facts.AssociationStatus == AssociationStatus.Unknown)
            {
                diagnostics.Add(new AuditDiagnostic(
                    "ASSOCIATION_STATE_UNKNOWN",
                    FindingSeverity.Warning,
                    "NX association state is unknown; association-dependent conclusions were suppressed.")
                {
                    AnnotationId = annotation.Id,
                    EvidenceId = annotation.AnnotationObject?.EvidenceId
                });
            }

            if (facts.Confidence != EvidenceConfidence.High)
            {
                diagnostics.Add(new AuditDiagnostic(
                    "EVIDENCE_CONFIDENCE_INSUFFICIENT",
                    FindingSeverity.Warning,
                    $"Evidence confidence is {facts.Confidence}; high confidence is required for evidence-dependent conclusions.")
                {
                    AnnotationId = annotation.Id,
                    EvidenceId = annotation.AnnotationObject?.EvidenceId
                });
            }

            if (facts.ExtractionState == ExtractionState.Complete &&
                facts.Confidence == EvidenceConfidence.High &&
                !IsCompleteReference(annotation.AnnotationObject))
            {
                diagnostics.Add(new AuditDiagnostic(
                    "ANNOTATION_REFERENCE_INCOMPLETE",
                    FindingSeverity.Warning,
                    "The annotation NX tag, run-scoped evidence ID, object type, or owning part is missing; evidence-dependent conclusions were suppressed.")
                {
                    AnnotationId = annotation.Id,
                    EvidenceId = annotation.AnnotationObject?.EvidenceId
                });
            }

            if (facts.ExtractionState == ExtractionState.Complete &&
                facts.AssociationStatus == AssociationStatus.Associated &&
                facts.Confidence == EvidenceConfidence.High)
            {
                if (!IsCompleteReference(facts.DefinedFeature))
                {
                    diagnostics.Add(new AuditDiagnostic(
                        "DEFINED_FEATURE_REFERENCE_INCOMPLETE",
                        FindingSeverity.Warning,
                        "The directly owning feature reference is incomplete; evidence-dependent conclusions were suppressed.")
                    {
                        AnnotationId = annotation.Id,
                        EvidenceId = annotation.AnnotationObject?.EvidenceId
                    });
                }

                if (facts.AssociatedGeometry.Count == 0 || facts.AssociatedGeometry.Any(item => !IsCompleteReference(item)))
                {
                    diagnostics.Add(new AuditDiagnostic(
                        "ASSOCIATED_GEOMETRY_REFERENCE_INCOMPLETE",
                        FindingSeverity.Warning,
                        "Associated geometry references are missing or incomplete; evidence-dependent conclusions were suppressed.")
                    {
                        AnnotationId = annotation.Id,
                        EvidenceId = annotation.AnnotationObject?.EvidenceId
                    });
                }
            }

            if (!annotation.IsReference &&
                facts.ExtractionState == ExtractionState.Complete &&
                facts.AssociationStatus == AssociationStatus.Associated &&
                facts.Confidence == EvidenceConfidence.High &&
                !facts.HasDirectDefinition)
            {
                diagnostics.Add(new AuditDiagnostic(
                    "DEFINITION_IDENTITY_INCOMPLETE",
                    FindingSeverity.Warning,
                    "Direct feature, characteristic, and target-subgeometry identity is incomplete; duplicate-definition evaluation was suppressed.")
                {
                    AnnotationId = annotation.Id,
                    EvidenceId = annotation.AnnotationObject?.EvidenceId,
                    RuleId = "DUPLICATE_FEATURE_DEFINITION"
                });
            }
        }

        return Deduplicate(diagnostics);
    }

    private static IReadOnlyList<AuditDiagnostic> Deduplicate(IEnumerable<AuditDiagnostic> diagnostics) =>
        diagnostics
            .DistinctBy(item => (item.Code, item.AnnotationId, item.EvidenceId, item.RuleId, item.Message))
            .ToArray();

    private static bool IsCompleteReference(NxObjectReference? reference) =>
        reference is not null &&
        !string.IsNullOrWhiteSpace(reference.EvidenceId) &&
        !string.IsNullOrWhiteSpace(reference.NxTag) &&
        !string.IsNullOrWhiteSpace(reference.ObjectType) &&
        !string.IsNullOrWhiteSpace(reference.OwningPart);
}

public sealed class DrawingCadValueMismatchRule : IAuditRule
{
    public string Id => "DRAWING_CAD_VALUE_MISMATCH";

    public IEnumerable<AuditFinding> Evaluate(AuditDocument document) =>
        document.SchemaVersion == "1.0" ? EvaluateLegacy(document) : EvaluateVersion11(document);

    private IEnumerable<AuditFinding> EvaluateLegacy(AuditDocument document)
    {
        foreach (var (_, _, annotation) in AuditTraversal.Annotations(document))
        {
            if (annotation.DisplayedValue is not { } displayed || annotation.CadMeasuredValue is not { } measured)
            {
                continue;
            }

            if (Math.Abs(displayed - measured) <= annotation.ComparisonTolerance)
            {
                continue;
            }

            yield return new AuditFinding(
                Id,
                FindingSeverity.Error,
                $"Drawing value {displayed:G} does not match CAD measurement {measured:G}.",
                annotation.Id,
                annotation.AssociatedFeatureIds.FirstOrDefault(),
                displayed.ToString("G17"),
                measured.ToString("G17"),
                annotation.Location);
        }
    }

    private IEnumerable<AuditFinding> EvaluateVersion11(AuditDocument document)
    {
        foreach (var (_, _, annotation) in AuditTraversal.Annotations(document))
        {
            if (annotation.Kind is not (AnnotationKind.Dimension or AnnotationKind.HoleCallout))
            {
                continue;
            }

            var facts = EffectiveAnnotationFactsResolver.Resolve(document, annotation);
            if (!facts.HasCompleteAuthoritativeCadEvidence ||
                facts.OverrideConflict ||
                facts.AutomaticValueConflict ||
                facts.ManualOverrideState == ManualOverrideState.Unknown)
            {
                continue;
            }

            var comparison = AnnotationValueComparer.Compare(facts);
            if (comparison.Status != ValueComparisonStatus.Mismatch)
            {
                continue;
            }

            var assessment = facts.ManualOverrideState == ManualOverrideState.Overridden
                ? AnnotationAssessment.ManualOverride
                : IsAssociativeButStale(facts)
                    ? AnnotationAssessment.AssociativeButStale
                    : AnnotationAssessment.Unknown;

            var message = facts.ValueKind == SemanticValueKind.DashDesignator
                ? $"Drawing designation {comparison.Observed} does not match authoritative CAD designation {comparison.Expected}; numeric difference is not applicable."
                : assessment == AnnotationAssessment.AssociativeButStale
                    ? $"Displayed value {comparison.Observed} agrees with the NX automatic value, but authoritative CAD value {comparison.Expected} differs."
                    : $"Drawing value {comparison.Observed} does not match authoritative CAD value {comparison.Expected} at the displayed resolution.";

            yield return new AuditFinding(
                Id,
                FindingSeverity.Error,
                message,
                annotation.Id,
                ResolveFeatureId(facts),
                comparison.Observed,
                comparison.Expected,
                annotation.Location)
            {
                Difference = comparison.Difference ?? "not applicable",
                Assessment = assessment,
                Confidence = facts.Confidence,
                EvidenceId = annotation.AnnotationObject?.EvidenceId
            };
        }
    }

    private static bool IsAssociativeButStale(EffectiveAnnotationFacts facts)
    {
        if (facts.ManualOverrideState != ManualOverrideState.NotOverridden ||
            facts.AssociationStatus != AssociationStatus.Associated)
        {
            return false;
        }

        var displayedAgreesWithAutomatic = AnnotationValueComparer.ValuesEquivalent(
            facts.ValueKind,
            facts.NumericComparisonMode,
            facts.DisplayedNumericValue,
            facts.NxAutomaticValue,
            facts.DisplayedDesignation ?? facts.RawDisplayedText,
            facts.NxAutomaticDesignation,
            facts.DisplayResolution,
            facts.ComparisonTolerance);
        var automaticAgreesWithCad = AnnotationValueComparer.ValuesEquivalent(
            facts.ValueKind,
            facts.NumericComparisonMode,
            facts.NxAutomaticValue,
            facts.CadValue,
            facts.NxAutomaticDesignation,
            facts.CadDesignation,
            facts.DisplayResolution,
            facts.ComparisonTolerance);
        return displayedAgreesWithAutomatic && !automaticAgreesWithCad;
    }

    private static string? ResolveFeatureId(EffectiveAnnotationFacts facts) =>
        facts.DefinitionIdentity?.FeatureId ??
        facts.DefinedFeature?.EvidenceId ??
        facts.Annotation.AssociatedFeatureIds.FirstOrDefault();
}

public sealed class ManualDimensionOverrideRule : IAuditRule
{
    public string Id => "MANUAL_DIMENSION_OVERRIDE";

    public IEnumerable<AuditFinding> Evaluate(AuditDocument document)
    {
        foreach (var (_, _, annotation) in AuditTraversal.Annotations(document))
        {
            if (annotation.Kind is not (AnnotationKind.Dimension or AnnotationKind.HoleCallout))
            {
                continue;
            }

            if (document.SchemaVersion == "1.0")
            {
                if (!annotation.IsManualOverride)
                {
                    continue;
                }

                yield return new AuditFinding(
                    Id,
                    FindingSeverity.Warning,
                    "Dimension text contains a manual override and requires engineering review.",
                    annotation.Id,
                    annotation.AssociatedFeatureIds.FirstOrDefault(),
                    annotation.DisplayedText,
                    "Associative automatic dimension text",
                    annotation.Location);
                continue;
            }

            var facts = EffectiveAnnotationFactsResolver.Resolve(document, annotation);
            if (facts.ManualOverrideState != ManualOverrideState.Overridden ||
                facts.OverrideConflict ||
                !facts.HasCompleteAnnotationReference ||
                facts.Confidence != EvidenceConfidence.High)
            {
                continue;
            }

            yield return new AuditFinding(
                Id,
                FindingSeverity.Warning,
                "NX reports a manual annotation override; engineering review is required.",
                annotation.Id,
                facts.DefinitionIdentity?.FeatureId ?? facts.DefinedFeature?.EvidenceId,
                facts.RawDisplayedText,
                facts.ValueKind == SemanticValueKind.DashDesignator
                    ? facts.NxAutomaticDesignation
                    : facts.NxAutomaticValue?.ToString("G17", CultureInfo.InvariantCulture),
                annotation.Location)
            {
                Assessment = AnnotationAssessment.ManualOverride,
                Confidence = facts.Confidence,
                EvidenceId = annotation.AnnotationObject?.EvidenceId
            };
        }
    }
}

public sealed class BrokenDimensionAssociativityRule : IAuditRule
{
    public string Id => "BROKEN_DIMENSION_ASSOCIATIVITY";

    public IEnumerable<AuditFinding> Evaluate(AuditDocument document)
    {
        foreach (var (_, _, annotation) in AuditTraversal.Annotations(document))
        {
            if (annotation.Kind is not (AnnotationKind.Dimension or AnnotationKind.HoleCallout))
            {
                continue;
            }

            if (document.SchemaVersion == "1.0")
            {
                if (annotation.AssociationStatus != AssociationStatus.Broken)
                {
                    continue;
                }

                yield return new AuditFinding(
                    Id,
                    FindingSeverity.Error,
                    "Dimension is not associated with authoritative model geometry.",
                    annotation.Id,
                    null,
                    "Broken association",
                    "Associated NX model geometry",
                    annotation.Location);
                continue;
            }

            var facts = EffectiveAnnotationFactsResolver.Resolve(document, annotation);
            if (facts.AssociationConflict ||
                facts.ExtractionState is ExtractionState.Unsupported or ExtractionState.Failed ||
                facts.Confidence != EvidenceConfidence.High ||
                facts.AssociationStatus is not (AssociationStatus.PartiallyAssociated or AssociationStatus.Broken))
            {
                continue;
            }

            var partial = facts.AssociationStatus == AssociationStatus.PartiallyAssociated;
            yield return new AuditFinding(
                Id,
                FindingSeverity.Error,
                partial
                    ? "Dimension has only partial association to authoritative model geometry."
                    : "Dimension is not associated with authoritative model geometry.",
                annotation.Id,
                facts.DefinitionIdentity?.FeatureId,
                partial ? "Partially associated" : "Broken association",
                "Fully associated NX model geometry",
                annotation.Location)
            {
                Assessment = partial ? AnnotationAssessment.PartiallyAssociated : AnnotationAssessment.BrokenAssociation,
                Confidence = facts.Confidence,
                EvidenceId = annotation.AnnotationObject?.EvidenceId
            };
        }
    }
}

public sealed class DuplicateFeatureDefinitionRule : IAuditRule
{
    public string Id => "DUPLICATE_FEATURE_DEFINITION";

    public IEnumerable<AuditFinding> Evaluate(AuditDocument document) =>
        document.SchemaVersion == "1.0" ? EvaluateLegacy(document) : EvaluateVersion11(document);

    private IEnumerable<AuditFinding> EvaluateLegacy(AuditDocument document)
    {
        var definitions = AuditTraversal.Annotations(document)
            .Where(item => !item.Annotation.IsReference)
            .Where(item => item.Annotation.Kind is AnnotationKind.Dimension or AnnotationKind.HoleCallout)
            .SelectMany(item => item.Annotation.AssociatedFeatureIds.Select(featureId => new
            {
                FeatureId = featureId,
                item.Annotation,
                item.View
            }))
            .GroupBy(item => item.FeatureId, StringComparer.Ordinal);

        foreach (var group in definitions)
        {
            var annotations = group.DistinctBy(item => item.Annotation.Id).ToArray();
            if (annotations.Length < 2)
            {
                continue;
            }

            var first = annotations[0];
            var locations = string.Join(", ", annotations.Select(item => $"{item.View.Name}/{item.Annotation.Id}"));
            yield return new AuditFinding(
                Id,
                FindingSeverity.Error,
                $"Feature is defined by multiple non-reference annotations: {locations}.",
                first.Annotation.Id,
                group.Key,
                annotations.Length.ToString(),
                "One defining annotation; additional occurrences marked reference when appropriate",
                first.Annotation.Location);
        }
    }

    private IEnumerable<AuditFinding> EvaluateVersion11(AuditDocument document)
    {
        var definitions = AuditTraversal.Annotations(document)
            .Where(item => !item.Annotation.IsReference)
            .Where(item => item.Annotation.Kind is AnnotationKind.Dimension or AnnotationKind.HoleCallout)
            .Select(item => new
            {
                item.Sheet,
                item.View,
                item.Annotation,
                Facts = EffectiveAnnotationFactsResolver.Resolve(document, item.Annotation)
            })
            .Where(item => item.Facts.HasCompleteDirectDefinitionEvidence)
            .Select(item => new DefinitionCandidate(
                item.Sheet,
                item.View,
                item.Annotation,
                item.Facts,
                item.Facts.DefinitionIdentity!))
            .ToArray();

        var exactGroups = definitions.GroupBy(
            item => new DefinitionKey(
                item.Identity.FeatureId,
                item.Identity.CharacteristicKey,
                NormalizeTargets(item.Identity.TargetSubgeometryIds)),
            DefinitionKeyComparer.Instance);

        foreach (var group in exactGroups)
        {
            var annotations = group.DistinctBy(item => item.Annotation.Id).ToArray();
            if (annotations.Length < 2)
            {
                continue;
            }

            var first = annotations[0];
            var locations = string.Join(", ", annotations.Select(item => $"{item.View.Name}/{item.Annotation.Id}"));
            yield return new AuditFinding(
                Id,
                FindingSeverity.Error,
                $"Feature characteristic '{group.Key.CharacteristicKey}' is defined by multiple non-reference annotations: {locations}.",
                first.Annotation.Id,
                group.Key.FeatureId,
                annotations.Length.ToString(CultureInfo.InvariantCulture),
                "One defining annotation; additional occurrences marked reference when appropriate",
                first.Annotation.Location)
            {
                Assessment = AnnotationAssessment.Consistent,
                Confidence = EvidenceConfidence.High,
                EvidenceId = first.Annotation.AnnotationObject?.EvidenceId
            };
        }

        foreach (var featureGroup in definitions.GroupBy(item => item.Identity.FeatureId, StringComparer.Ordinal))
        {
            var characteristicKeys = featureGroup
                .Select(item => item.Identity.CharacteristicKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (characteristicKeys.Length < 2)
            {
                continue;
            }

            var first = featureGroup.First();
            yield return new AuditFinding(
                Id,
                FindingSeverity.Information,
                $"Feature is annotated for different characteristics ({string.Join(", ", characteristicKeys)}); these definitions require independent review but are not a duplicate-definition error.",
                first.Annotation.Id,
                featureGroup.Key,
                string.Join(",", characteristicKeys),
                "Characteristics reviewed independently",
                first.Annotation.Location)
            {
                Confidence = EvidenceConfidence.High,
                EvidenceId = first.Annotation.AnnotationObject?.EvidenceId
            };
        }
    }

    private static string NormalizeTargets(IEnumerable<string> targets) =>
        string.Join("\u001F", targets
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal));

    private sealed record DefinitionCandidate(
        DrawingSheet Sheet,
        DrawingView View,
        DrawingAnnotation Annotation,
        EffectiveAnnotationFacts Facts,
        DefinitionIdentity Identity);

    private sealed record DefinitionKey(
        string FeatureId,
        string CharacteristicKey,
        string NormalizedTargets);

    private sealed class DefinitionKeyComparer : IEqualityComparer<DefinitionKey>
    {
        public static DefinitionKeyComparer Instance { get; } = new();

        public bool Equals(DefinitionKey? x, DefinitionKey? y) =>
            ReferenceEquals(x, y) ||
            x is not null && y is not null &&
            string.Equals(x.FeatureId, y.FeatureId, StringComparison.Ordinal) &&
            string.Equals(x.CharacteristicKey, y.CharacteristicKey, StringComparison.Ordinal) &&
            string.Equals(x.NormalizedTargets, y.NormalizedTargets, StringComparison.Ordinal);

        public int GetHashCode(DefinitionKey obj)
        {
            var hash = new HashCode();
            hash.Add(obj.FeatureId, StringComparer.Ordinal);
            hash.Add(obj.CharacteristicKey, StringComparer.Ordinal);
            hash.Add(obj.NormalizedTargets, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}

public sealed class FlatnessReferencesDatumRule : IAuditRule
{
    public string Id => "FLATNESS_REFERENCES_DATUM";

    public IEnumerable<AuditFinding> Evaluate(AuditDocument document)
    {
        foreach (var (_, _, annotation) in AuditTraversal.Annotations(document))
        {
            if (document.SchemaVersion != "1.0")
            {
                continue;
            }

            if (annotation.Gdt is not { Characteristic: GdtCharacteristic.Flatness } frame ||
                frame.DatumReferences.Count == 0)
            {
                continue;
            }

            yield return new AuditFinding(
                Id,
                FindingSeverity.Error,
                $"Flatness feature-control frame references datum(s): {string.Join(", ", frame.DatumReferences)}.",
                annotation.Id,
                annotation.DefinitionIdentity?.FeatureId ?? annotation.AssociatedFeatureIds.FirstOrDefault(),
                string.Join(",", frame.DatumReferences),
                "No datum references",
                annotation.Location);
        }
    }
}
