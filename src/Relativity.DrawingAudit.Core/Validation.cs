using System.Text.Json;

namespace Relativity.DrawingAudit.Core;

public sealed record AuditValidationIssue(
    string Path,
    string Code,
    string Message);

public sealed class AuditDocumentValidationException : Exception
{
    public AuditDocumentValidationException(IReadOnlyList<AuditValidationIssue> issues)
        : base(CreateMessage(issues))
    {
        Issues = issues;
    }

    public IReadOnlyList<AuditValidationIssue> Issues { get; }

    private static string CreateMessage(IReadOnlyList<AuditValidationIssue> issues) =>
        issues.Count == 0
            ? "The audit document is invalid."
            : $"The audit document is invalid: {string.Join("; ", issues.Select(issue => $"{issue.Path}: {issue.Message}"))}";
}

public static class AuditDocumentLoader
{
    public static AuditDocument Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var parsed = JsonDocument.Parse(json);
        return LoadParsed(parsed);
    }

    public static AuditDocument Load(Stream utf8Json)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        using var parsed = JsonDocument.Parse(utf8Json);
        return LoadParsed(parsed);
    }

    public static async Task<AuditDocument> LoadAsync(
        Stream utf8Json,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        using var parsed = await JsonDocument.ParseAsync(utf8Json, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return LoadParsed(parsed);
    }

    private static AuditDocument LoadParsed(JsonDocument parsed)
    {
        var versionIssues = ValidateVersionToken(parsed.RootElement);
        if (versionIssues.Count != 0)
        {
            throw new AuditDocumentValidationException(versionIssues);
        }

        var schemaVersion = parsed.RootElement.GetProperty("schemaVersion").GetString();
        var serializerOptions = schemaVersion == "1.1"
            ? AuditJson.Version11Options
            : AuditJson.Options;
        var document = parsed.RootElement.Deserialize<AuditDocument>(serializerOptions)
            ?? throw new AuditDocumentValidationException(
            [
                new AuditValidationIssue("$", "DOCUMENT_EMPTY", "Input did not contain an audit document.")
            ]);

        AuditDocumentValidator.ValidateAndThrow(document);
        return document;
    }

    private static IReadOnlyList<AuditValidationIssue> ValidateVersionToken(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return
            [
                new AuditValidationIssue("$", "DOCUMENT_NOT_OBJECT", "The root value must be a JSON object.")
            ];
        }

        if (!root.TryGetProperty("schemaVersion", out var token) || token.ValueKind != JsonValueKind.String)
        {
            return
            [
                new AuditValidationIssue("$.schemaVersion", "SCHEMA_VERSION_REQUIRED", "schemaVersion is required and must be a string.")
            ];
        }

        var version = token.GetString();
        return version is "1.0" or "1.1"
            ? Array.Empty<AuditValidationIssue>()
            :
            [
                new AuditValidationIssue("$.schemaVersion", "SCHEMA_VERSION_UNSUPPORTED", $"Unsupported schema version '{version}'.")
            ];
    }
}

public static class AuditDocumentValidator
{
    public static IReadOnlyList<AuditValidationIssue> Validate(AuditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<AuditValidationIssue>();

        if (document.SchemaVersion is not ("1.0" or "1.1"))
        {
            issues.Add(new AuditValidationIssue(
                "$.schemaVersion",
                string.IsNullOrWhiteSpace(document.SchemaVersion) ? "SCHEMA_VERSION_REQUIRED" : "SCHEMA_VERSION_UNSUPPORTED",
                string.IsNullOrWhiteSpace(document.SchemaVersion)
                    ? "schemaVersion is required."
                    : $"Unsupported schema version '{document.SchemaVersion}'."));
            return issues;
        }

        ValidateCommon(document, issues);
        if (document.SchemaVersion == "1.1")
        {
            ValidateVersion11(document, issues);
        }

        return issues;
    }

    public static void ValidateAndThrow(AuditDocument document)
    {
        var issues = Validate(document);
        if (issues.Count != 0)
        {
            throw new AuditDocumentValidationException(issues);
        }
    }

    private static void ValidateCommon(AuditDocument document, List<AuditValidationIssue> issues)
    {
        ValidateDiagnostics(document.Diagnostics, "$.diagnostics", issues);

        if (document.Drawing is null)
        {
            issues.Add(new AuditValidationIssue("$.drawing", "DRAWING_REQUIRED", "Drawing identity is required."));
        }
        else
        {
            RequireText(document.Drawing.PartNumber, "$.drawing.partNumber", "PART_NUMBER_REQUIRED", issues);
            RequireText(document.Drawing.Revision, "$.drawing.revision", "REVISION_REQUIRED", issues);
            RequireText(document.Drawing.Units, "$.drawing.units", "DRAWING_UNITS_REQUIRED", issues);
            RequireText(document.Drawing.StandardProfile, "$.drawing.standardProfile", "STANDARD_PROFILE_REQUIRED", issues);
        }

        if (document.Sheets is null)
        {
            issues.Add(new AuditValidationIssue("$.sheets", "SHEETS_REQUIRED", "Sheets are required."));
            return;
        }

        var sheetIds = new HashSet<string>(StringComparer.Ordinal);
        var annotationIds = new HashSet<string>(StringComparer.Ordinal);
        for (var sheetIndex = 0; sheetIndex < document.Sheets.Count; sheetIndex++)
        {
            var sheet = document.Sheets[sheetIndex];
            var sheetPath = $"$.sheets[{sheetIndex}]";
            if (sheet is null)
            {
                issues.Add(new AuditValidationIssue(sheetPath, "SHEET_REQUIRED", "Sheet entry cannot be null."));
                continue;
            }

            RequireText(sheet.Id, $"{sheetPath}.id", "SHEET_ID_REQUIRED", issues);
            if (!string.IsNullOrWhiteSpace(sheet.Id) && !sheetIds.Add(sheet.Id))
            {
                issues.Add(new AuditValidationIssue($"{sheetPath}.id", "SHEET_ID_DUPLICATE", $"Duplicate sheet ID '{sheet.Id}'."));
            }

            if (sheet.Views is null)
            {
                issues.Add(new AuditValidationIssue($"{sheetPath}.views", "VIEWS_REQUIRED", "Views are required."));
                continue;
            }

            var viewIds = new HashSet<string>(StringComparer.Ordinal);
            for (var viewIndex = 0; viewIndex < sheet.Views.Count; viewIndex++)
            {
                var view = sheet.Views[viewIndex];
                var viewPath = $"{sheetPath}.views[{viewIndex}]";
                if (view is null)
                {
                    issues.Add(new AuditValidationIssue(viewPath, "VIEW_REQUIRED", "View entry cannot be null."));
                    continue;
                }

                RequireText(view.Id, $"{viewPath}.id", "VIEW_ID_REQUIRED", issues);
                if (!double.IsFinite(view.Scale) || view.Scale <= 0)
                {
                    issues.Add(new AuditValidationIssue($"{viewPath}.scale", "VIEW_SCALE_INVALID", "View scale must be finite and positive."));
                }
                if (!string.IsNullOrWhiteSpace(view.Id) && !viewIds.Add(view.Id))
                {
                    issues.Add(new AuditValidationIssue($"{viewPath}.id", "VIEW_ID_DUPLICATE", $"Duplicate view ID '{view.Id}'."));
                }

                if (view.Annotations is null)
                {
                    issues.Add(new AuditValidationIssue($"{viewPath}.annotations", "ANNOTATIONS_REQUIRED", "Annotations are required."));
                    continue;
                }

                for (var annotationIndex = 0; annotationIndex < view.Annotations.Count; annotationIndex++)
                {
                    var annotation = view.Annotations[annotationIndex];
                    var annotationPath = $"{viewPath}.annotations[{annotationIndex}]";
                    if (annotation is null)
                    {
                        issues.Add(new AuditValidationIssue(annotationPath, "ANNOTATION_REQUIRED", "Annotation entry cannot be null."));
                        continue;
                    }

                    RequireText(annotation.Id, $"{annotationPath}.id", "ANNOTATION_ID_REQUIRED", issues);
                    RequireDefinedEnum(annotation.Kind, $"{annotationPath}.kind", "ANNOTATION_KIND_INVALID", issues);
                    RequireDefinedEnum(annotation.AssociationStatus, $"{annotationPath}.associationStatus", "ASSOCIATION_STATE_INVALID", issues);
                    if (!double.IsFinite(annotation.ComparisonTolerance) || annotation.ComparisonTolerance < 0)
                    {
                        issues.Add(new AuditValidationIssue($"{annotationPath}.comparisonTolerance", "COMPARISON_TOLERANCE_INVALID", "Comparison tolerance must be finite and nonnegative."));
                    }

                    if (annotation.AssociatedFeatureIds is null)
                    {
                        issues.Add(new AuditValidationIssue($"{annotationPath}.associatedFeatureIds", "ASSOCIATED_FEATURE_IDS_REQUIRED", "Associated feature IDs must be an array."));
                    }
                    else
                    {
                        RequireNonEmptyStrings(
                            annotation.AssociatedFeatureIds,
                            $"{annotationPath}.associatedFeatureIds",
                            "ASSOCIATED_FEATURE_ID_INVALID",
                            issues);
                    }

                    if (annotation.Gdt is { } gdt)
                    {
                        RequireDefinedEnum(gdt.Characteristic, $"{annotationPath}.gdt.characteristic", "GDT_CHARACTERISTIC_INVALID", issues);
                        if (gdt.DatumReferences is null || gdt.Modifiers is null)
                        {
                            issues.Add(new AuditValidationIssue($"{annotationPath}.gdt", "GDT_COLLECTIONS_REQUIRED", "GD&T datum references and modifiers must be arrays."));
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(annotation.Id) && !annotationIds.Add(annotation.Id))
                    {
                        issues.Add(new AuditValidationIssue($"{annotationPath}.id", "ANNOTATION_ID_DUPLICATE", $"Annotation ID '{annotation.Id}' is duplicated elsewhere in the document."));
                    }
                }
            }
        }

        if (document.Features is null)
        {
            issues.Add(new AuditValidationIssue("$.features", "FEATURES_REQUIRED", "Features are required."));
        }
        else
        {
            var featureIds = new HashSet<string>(StringComparer.Ordinal);
            for (var featureIndex = 0; featureIndex < document.Features.Count; featureIndex++)
            {
                var feature = document.Features[featureIndex];
                var featurePath = $"$.features[{featureIndex}]";
                if (feature is null)
                {
                    issues.Add(new AuditValidationIssue(featurePath, "FEATURE_REQUIRED", "Feature entry cannot be null."));
                    continue;
                }

                RequireText(feature.Id, $"{featurePath}.id", "FEATURE_ID_REQUIRED", issues);
                if (!string.IsNullOrWhiteSpace(feature.Id) && !featureIds.Add(feature.Id))
                {
                    issues.Add(new AuditValidationIssue($"{featurePath}.id", "FEATURE_ID_DUPLICATE", $"Feature ID '{feature.Id}' is duplicated."));
                }
                RequireText(feature.NxTag, $"{featurePath}.nxTag", "FEATURE_NX_TAG_REQUIRED", issues);
                RequireText(feature.FeatureType, $"{featurePath}.featureType", "FEATURE_TYPE_REQUIRED", issues);
                RequireText(feature.OwningPart, $"{featurePath}.owningPart", "FEATURE_OWNING_PART_REQUIRED", issues);
            }
        }
    }

    private static void ValidateVersion11(AuditDocument document, List<AuditValidationIssue> issues)
    {
        if (document.ExtractionMetadata is null)
        {
            issues.Add(new AuditValidationIssue(
                "$.extractionMetadata",
                "EXTRACTION_METADATA_REQUIRED",
                "Schema 1.1 requires extraction metadata."));
        }
        else
        {
            RequireText(document.ExtractionMetadata.SourceSystem, "$.extractionMetadata.sourceSystem", "SOURCE_SYSTEM_REQUIRED", issues);
            RequireText(document.ExtractionMetadata.ExtractorVersion, "$.extractionMetadata.extractorVersion", "EXTRACTOR_VERSION_REQUIRED", issues);
            RequireText(document.ExtractionMetadata.RunId, "$.extractionMetadata.runId", "RUN_ID_REQUIRED", issues);
            if (document.ExtractionMetadata.ExtractedAtUtc == default)
            {
                issues.Add(new AuditValidationIssue(
                    "$.extractionMetadata.extractedAtUtc",
                    "EXTRACTION_TIME_REQUIRED",
                    "A non-default UTC extraction time is required."));
            }

            if (document.ExtractionMetadata.DrawingModifiedBefore != document.ExtractionMetadata.DrawingModifiedAfter ||
                document.ExtractionMetadata.ReferencedModelModifiedBefore != document.ExtractionMetadata.ReferencedModelModifiedAfter)
            {
                issues.Add(new AuditValidationIssue(
                    "$.extractionMetadata",
                    "NX_MODIFIED_STATE_CHANGED",
                    "Drawing and referenced-model modified-state flags must be unchanged by extraction."));
            }
        }

        if (document.Sheets is null)
        {
            return;
        }

        for (var sheetIndex = 0; sheetIndex < document.Sheets.Count; sheetIndex++)
        {
            var sheet = document.Sheets[sheetIndex];
            if (sheet?.Views is null)
            {
                continue;
            }

            for (var viewIndex = 0; viewIndex < sheet.Views.Count; viewIndex++)
            {
                var view = sheet.Views[viewIndex];
                var viewPath = $"$.sheets[{sheetIndex}].views[{viewIndex}]";
                if (view is null)
                {
                    continue;
                }

                RequireText(view.EvidenceId, $"{viewPath}.evidenceId", "VIEW_EVIDENCE_ID_REQUIRED", issues);
                RequireText(view.Orientation, $"{viewPath}.orientation", "VIEW_ORIENTATION_REQUIRED", issues);
                if (view.Transform is null)
                {
                    issues.Add(new AuditValidationIssue($"{viewPath}.transform", "VIEW_TRANSFORM_REQUIRED", "Schema 1.1 requires a view transform."));
                }

                if (view.ReferencedPartIdentity is null)
                {
                    issues.Add(new AuditValidationIssue(
                        $"{viewPath}.referencedPartIdentity",
                        "REFERENCED_PART_IDENTITY_REQUIRED",
                        "Schema 1.1 requires direct referenced-part identity evidence."));
                }

                if (view.Annotations is null)
                {
                    continue;
                }

                for (var annotationIndex = 0; annotationIndex < view.Annotations.Count; annotationIndex++)
                {
                    var annotation = view.Annotations[annotationIndex];
                    if (annotation is null)
                    {
                        continue;
                    }

                    var annotationPath = $"{viewPath}.annotations[{annotationIndex}]";
                    if (annotation.AssociationStatus == AssociationStatus.Unsupported)
                    {
                        issues.Add(new AuditValidationIssue(
                            $"{annotationPath}.associationStatus",
                            "ASSOCIATION_UNSUPPORTED_LEGACY_ONLY",
                            "Schema 1.1 represents unsupported extraction on extractionState, not associationStatus."));
                    }

                    if (annotation.Kind is AnnotationKind.Dimension or AnnotationKind.HoleCallout)
                    {
                        ValidateValueEvidence(annotation, annotationPath, issues);
                    }
                }
            }
        }

        ValidateEvidenceReferences(document, issues);
    }

    private static void ValidateEvidenceReferences(
        AuditDocument document,
        List<AuditValidationIssue> issues)
    {
        var identities = new Dictionary<string, EvidenceReferenceIdentity>(StringComparer.Ordinal);
        var annotationEvidenceIds = new HashSet<string>(StringComparer.Ordinal);
        var viewEvidenceIds = new HashSet<string>(StringComparer.Ordinal);

        if (document.Features is not null)
        {
            for (var featureIndex = 0; featureIndex < document.Features.Count; featureIndex++)
            {
                var feature = document.Features[featureIndex];
                if (feature?.ObjectReference is { } reference)
                {
                    ValidateEvidenceReference(reference, $"$.features[{featureIndex}].objectReference", identities, issues);
                    if (!string.Equals(feature.Id, reference.EvidenceId, StringComparison.Ordinal) ||
                        !string.Equals(feature.NxTag, reference.NxTag, StringComparison.Ordinal) ||
                        !string.Equals(feature.OwningPart, reference.OwningPart, StringComparison.Ordinal))
                    {
                        issues.Add(new AuditValidationIssue(
                            $"$.features[{featureIndex}].objectReference",
                            "FEATURE_REFERENCE_CONFLICT",
                            "CAD feature fields disagree with their NX object reference."));
                    }
                }
            }
        }

        if (document.Sheets is null)
        {
            return;
        }

        for (var sheetIndex = 0; sheetIndex < document.Sheets.Count; sheetIndex++)
        {
            var sheet = document.Sheets[sheetIndex];
            if (sheet?.Views is null)
            {
                continue;
            }

            for (var viewIndex = 0; viewIndex < sheet.Views.Count; viewIndex++)
            {
                var view = sheet.Views[viewIndex];
                if (view is null)
                {
                    continue;
                }

                var viewPath = $"$.sheets[{sheetIndex}].views[{viewIndex}]";
                if (!string.IsNullOrWhiteSpace(view.EvidenceId) && !viewEvidenceIds.Add(view.EvidenceId))
                {
                    issues.Add(new AuditValidationIssue($"{viewPath}.evidenceId", "VIEW_EVIDENCE_ID_DUPLICATE", $"View evidence ID '{view.EvidenceId}' is duplicated."));
                }

                if (view.Transform is { } transform &&
                    (transform.Matrix is not { Count: 9 } || transform.Translation is not { Count: 3 }))
                {
                    issues.Add(new AuditValidationIssue($"{viewPath}.transform", "VIEW_TRANSFORM_INVALID", "A view transform requires a 3x3 matrix and three-value translation."));
                }

                if (view.ReferencedPartIdentity is { } partReference)
                {
                    ValidateEvidenceReference(partReference, $"{viewPath}.referencedPartIdentity", identities, issues);
                }

                if (view.Annotations is null)
                {
                    continue;
                }

                for (var annotationIndex = 0; annotationIndex < view.Annotations.Count; annotationIndex++)
                {
                    var annotation = view.Annotations[annotationIndex];
                    if (annotation is null)
                    {
                        continue;
                    }

                    var annotationPath = $"{viewPath}.annotations[{annotationIndex}]";
                    if (annotation.AnnotationObject is { } annotationReference)
                    {
                        ValidateEvidenceReference(annotationReference, $"{annotationPath}.annotationObject", identities, issues);
                        if (!string.IsNullOrWhiteSpace(annotationReference.EvidenceId) &&
                            !annotationEvidenceIds.Add(annotationReference.EvidenceId))
                        {
                            issues.Add(new AuditValidationIssue(
                                $"{annotationPath}.annotationObject.evidenceId",
                                "ANNOTATION_EVIDENCE_ID_DUPLICATE",
                                $"Annotation evidence ID '{annotationReference.EvidenceId}' is duplicated."));
                        }
                    }

                    if (annotation.DefinitionIdentity?.TargetSubgeometryIds is null && annotation.DefinitionIdentity is not null)
                    {
                        issues.Add(new AuditValidationIssue(
                            $"{annotationPath}.definitionIdentity.targetSubgeometryIds",
                            "TARGET_SUBGEOMETRY_IDS_REQUIRED",
                            "Target subgeometry IDs must be an array."));
                    }
                    else if (annotation.DefinitionIdentity?.TargetSubgeometryIds is { } targetIds)
                    {
                        RequireNonEmptyStrings(
                            targetIds,
                            $"{annotationPath}.definitionIdentity.targetSubgeometryIds",
                            "TARGET_SUBGEOMETRY_ID_INVALID",
                            issues);
                    }

                    var evidence = annotation.ValueEvidence;
                    if (evidence?.DefinedFeature is { } definedFeature)
                    {
                        ValidateEvidenceReference(definedFeature, $"{annotationPath}.valueEvidence.definedFeature", identities, issues);
                    }

                    if (evidence?.AssociatedGeometry is not null)
                    {
                        for (var geometryIndex = 0; geometryIndex < evidence.AssociatedGeometry.Count; geometryIndex++)
                        {
                            var geometry = evidence.AssociatedGeometry[geometryIndex];
                            if (geometry is null)
                            {
                                issues.Add(new AuditValidationIssue(
                                    $"{annotationPath}.valueEvidence.associatedGeometry[{geometryIndex}]",
                                    "GEOMETRY_REFERENCE_REQUIRED",
                                    "Associated geometry entry cannot be null."));
                                continue;
                            }

                            ValidateEvidenceReference(
                                geometry,
                                $"{annotationPath}.valueEvidence.associatedGeometry[{geometryIndex}]",
                                identities,
                                issues);
                        }
                    }
                }
            }
        }
    }

    private static void ValidateEvidenceReference(
        NxObjectReference reference,
        string path,
        Dictionary<string, EvidenceReferenceIdentity> identities,
        List<AuditValidationIssue> issues)
    {
        RequireText(reference.EvidenceId, $"{path}.evidenceId", "EVIDENCE_ID_REQUIRED", issues);
        RequireText(reference.NxTag, $"{path}.nxTag", "NX_TAG_REQUIRED", issues);
        RequireText(reference.ObjectType, $"{path}.objectType", "NX_OBJECT_TYPE_REQUIRED", issues);
        RequireText(reference.OwningPart, $"{path}.owningPart", "NX_OWNING_PART_REQUIRED", issues);
        if (reference.DirectOwnerEvidenceIds is null)
        {
            issues.Add(new AuditValidationIssue($"{path}.directOwnerEvidenceIds", "DIRECT_OWNER_IDS_REQUIRED", "Direct-owner evidence IDs must be an array."));
        }
        else
        {
            RequireNonEmptyStrings(
                reference.DirectOwnerEvidenceIds,
                $"{path}.directOwnerEvidenceIds",
                "DIRECT_OWNER_ID_INVALID",
                issues);
        }

        if (string.IsNullOrWhiteSpace(reference.EvidenceId))
        {
            return;
        }

        var identity = new EvidenceReferenceIdentity(
            reference.NxTag,
            reference.ObjectType,
            reference.OwningPart,
            reference.OwningRevision);
        if (identities.TryGetValue(reference.EvidenceId, out var prior) && prior != identity)
        {
            issues.Add(new AuditValidationIssue(
                $"{path}.evidenceId",
                "EVIDENCE_IDENTITY_CONFLICT",
                $"Evidence ID '{reference.EvidenceId}' is associated with conflicting NX identity data."));
        }
        else
        {
            identities[reference.EvidenceId] = identity;
        }
    }

    private sealed record EvidenceReferenceIdentity(
        string NxTag,
        string ObjectType,
        string OwningPart,
        string? OwningRevision);

    private static void ValidateValueEvidence(
        DrawingAnnotation annotation,
        string annotationPath,
        List<AuditValidationIssue> issues)
    {
        var evidence = annotation.ValueEvidence;
        if (evidence is null)
        {
            issues.Add(new AuditValidationIssue(
                $"{annotationPath}.valueEvidence",
                "VALUE_EVIDENCE_REQUIRED",
                "Schema 1.1 dimensions and hole callouts require value evidence."));
            return;
        }

        var evidencePath = $"{annotationPath}.valueEvidence";
        ValidateDiagnostics(evidence.Diagnostics, $"{evidencePath}.diagnostics", issues);
        if (evidence.ValueKind is { } valueKind)
        {
            RequireDefinedEnum(valueKind, $"{evidencePath}.valueKind", "VALUE_KIND_INVALID", issues);
        }

        if (evidence.NumericComparisonMode is { } numericMode)
        {
            RequireDefinedEnum(numericMode, $"{evidencePath}.numericComparisonMode", "NUMERIC_COMPARISON_MODE_INVALID", issues);
        }

        if (evidence.ManualOverrideState is { } overrideState)
        {
            RequireDefinedEnum(overrideState, $"{evidencePath}.manualOverrideState", "OVERRIDE_STATE_INVALID", issues);
        }

        if (evidence.AssociationStatus is { } associationState)
        {
            RequireDefinedEnum(associationState, $"{evidencePath}.associationStatus", "ASSOCIATION_STATE_INVALID", issues);
        }

        if (evidence.ExtractionState is { } extractionState)
        {
            RequireDefinedEnum(extractionState, $"{evidencePath}.extractionState", "EXTRACTION_STATE_INVALID", issues);
        }

        if (evidence.Confidence is { } confidence)
        {
            RequireDefinedEnum(confidence, $"{evidencePath}.confidence", "EVIDENCE_CONFIDENCE_INVALID", issues);
        }

        if (evidence.Authority is { } authority)
        {
            RequireDefinedEnum(authority, $"{evidencePath}.authority", "EVIDENCE_AUTHORITY_INVALID", issues);
        }

        if (evidence.AssociatedGeometry is null)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.associatedGeometry", "ASSOCIATED_GEOMETRY_ARRAY_REQUIRED", "Associated geometry must be an array."));
        }

        if (evidence.CandidateCadDesignations is not null)
        {
            RequireNonEmptyStrings(
                evidence.CandidateCadDesignations,
                $"{evidencePath}.candidateCadDesignations",
                "PORT_DESIGNATION_CANDIDATE_INVALID",
                issues);
        }

        if (evidence.ExtractionState is null)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.extractionState", "EXTRACTION_STATE_REQUIRED", "Extraction state is required."));
        }

        if (evidence.AssociationStatus is null)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.associationStatus", "ASSOCIATION_STATE_REQUIRED", "Association state is required."));
        }
        else if (evidence.AssociationStatus == AssociationStatus.Unsupported)
        {
            issues.Add(new AuditValidationIssue(
                $"{evidencePath}.associationStatus",
                "ASSOCIATION_UNSUPPORTED_LEGACY_ONLY",
                "Schema 1.1 represents unsupported extraction on extractionState, not associationStatus."));
        }

        if (evidence.ExtractionState is not ExtractionState.Complete)
        {
            return;
        }

        if (evidence.ValueKind is null or SemanticValueKind.Unknown)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.valueKind", "VALUE_KIND_REQUIRED", "Complete evidence requires an explicit semantic value kind."));
        }

        if (evidence.ManualOverrideState is null or ManualOverrideState.Unknown)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.manualOverrideState", "OVERRIDE_STATE_REQUIRED", "Complete evidence requires a direct override state."));
        }

        if (evidence.Confidence is null or EvidenceConfidence.Unknown)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.confidence", "CONFIDENCE_REQUIRED", "Complete evidence requires an evidence confidence."));
        }

        if (evidence.Authority is null or EvidenceAuthority.Unknown)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.authority", "EVIDENCE_AUTHORITY_REQUIRED", "Complete evidence requires an authoritative source classification."));
        }

        RequireText(evidence.RawDisplayedText, $"{evidencePath}.rawDisplayedText", "RAW_DISPLAYED_TEXT_REQUIRED", issues);
        RequireText(evidence.MeasurementProvenance, $"{evidencePath}.measurementProvenance", "MEASUREMENT_PROVENANCE_REQUIRED", issues);

        if (evidence.AssociationStatus == AssociationStatus.Associated)
        {
            if (evidence.DefinedFeature is null)
            {
                issues.Add(new AuditValidationIssue($"{evidencePath}.definedFeature", "DEFINED_FEATURE_REQUIRED", "Complete associated evidence requires a directly identified owning feature."));
            }

            if (evidence.AssociatedGeometry is null or { Count: 0 })
            {
                issues.Add(new AuditValidationIssue($"{evidencePath}.associatedGeometry", "ASSOCIATED_GEOMETRY_REQUIRED", "Complete associated evidence requires associated model geometry."));
            }
        }

        switch (evidence.ValueKind)
        {
            case SemanticValueKind.DashDesignator:
                RequireText(evidence.ParsedDesignation, $"{evidencePath}.parsedDesignation", "DISPLAYED_DESIGNATION_REQUIRED", issues);
                RequireText(evidence.CadDesignation, $"{evidencePath}.cadDesignation", "CAD_DESIGNATION_REQUIRED", issues);
                RequireText(evidence.NxAutomaticDesignation, $"{evidencePath}.nxAutomaticDesignation", "NX_AUTOMATIC_DESIGNATION_REQUIRED", issues);
                break;

            case SemanticValueKind.NumericMagnitude:
                ValidateNumericEvidence(evidence, evidencePath, NumericComparisonMode.Magnitude, issues);
                break;

            case SemanticValueKind.SignedCoordinate:
                ValidateNumericEvidence(evidence, evidencePath, NumericComparisonMode.Signed, issues);
                break;
        }
    }

    private static void ValidateNumericEvidence(
        AnnotationValueEvidence evidence,
        string evidencePath,
        NumericComparisonMode expectedMode,
        List<AuditValidationIssue> issues)
    {
        if (evidence.NumericComparisonMode != expectedMode)
        {
            issues.Add(new AuditValidationIssue(
                $"{evidencePath}.numericComparisonMode",
                "NUMERIC_COMPARISON_MODE_REQUIRED",
                $"{evidence.ValueKind} requires numericComparisonMode '{expectedMode}'."));
        }

        if (evidence.ParsedNumericValue is null)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.parsedNumericValue", "DISPLAYED_NUMERIC_VALUE_REQUIRED", "A parsed displayed value is required."));
        }

        if (evidence.CadValue is null)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.cadValue", "CAD_NUMERIC_VALUE_REQUIRED", "An authoritative CAD value is required."));
        }

        if (evidence.NxAutomaticValue is null)
        {
            issues.Add(new AuditValidationIssue($"{evidencePath}.nxAutomaticValue", "NX_AUTOMATIC_VALUE_REQUIRED", "The NX automatic value is required."));
        }

        RequireText(evidence.Units, $"{evidencePath}.units", "VALUE_UNITS_REQUIRED", issues);
        RequireText(evidence.CadUnits, $"{evidencePath}.cadUnits", "CAD_VALUE_UNITS_REQUIRED", issues);
        if (evidence.DisplayResolution is null or <= 0)
        {
            issues.Add(new AuditValidationIssue(
                $"{evidencePath}.displayResolution",
                "DISPLAY_RESOLUTION_REQUIRED",
                "A positive display resolution from verified NX formatting preferences is required."));
        }
    }

    private static void RequireText(
        string? value,
        string path,
        string code,
        List<AuditValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new AuditValidationIssue(path, code, "A non-empty value is required."));
        }
    }

    private static void RequireNonEmptyStrings(
        IReadOnlyList<string> values,
        string path,
        string code,
        List<AuditValidationIssue> issues)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(values[index]))
            {
                issues.Add(new AuditValidationIssue($"{path}[{index}]", code, "Array entry must be a non-empty string."));
            }
        }
    }

    private static void ValidateDiagnostics(
        IReadOnlyList<AuditDiagnostic>? diagnostics,
        string path,
        List<AuditValidationIssue> issues)
    {
        if (diagnostics is null)
        {
            return;
        }

        for (var index = 0; index < diagnostics.Count; index++)
        {
            var diagnostic = diagnostics[index];
            var diagnosticPath = $"{path}[{index}]";
            if (diagnostic is null)
            {
                issues.Add(new AuditValidationIssue(diagnosticPath, "DIAGNOSTIC_REQUIRED", "Diagnostic entry cannot be null."));
                continue;
            }

            RequireText(diagnostic.Code, $"{diagnosticPath}.code", "DIAGNOSTIC_CODE_REQUIRED", issues);
            RequireText(diagnostic.Message, $"{diagnosticPath}.message", "DIAGNOSTIC_MESSAGE_REQUIRED", issues);
            RequireDefinedEnum(diagnostic.Severity, $"{diagnosticPath}.severity", "DIAGNOSTIC_SEVERITY_INVALID", issues);
        }
    }

    private static void RequireDefinedEnum<TEnum>(
        TEnum value,
        string path,
        string code,
        List<AuditValidationIssue> issues)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            issues.Add(new AuditValidationIssue(path, code, $"'{value}' is not a defined {typeof(TEnum).Name} value."));
        }
    }
}
