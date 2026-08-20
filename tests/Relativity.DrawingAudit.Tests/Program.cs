using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Relativity.DrawingAudit.Core;

var tests = new (string Name, Action Test)[]
{
    // Preserve the original five regression names and their schema 1.0 behavior.
    ("minus 12 drawing versus minus 16 CAD", DrawingCadMismatch),
    ("same hole defined in left and right views", DuplicateHole),
    ("left view wrong while right view matches", ViewSpecificMismatch),
    ("flatness references two datums", FlatnessWithDatums),
    ("reference dimension is not duplicate definition", ReferenceDimensionAllowed),

    ("versioned loader accepts 1.0 and 1.1", VersionedLoaderAcceptsKnownVersions),
    ("versioned loader rejects missing schema version", VersionedLoaderRejectsMissingVersion),
    ("versioned loader rejects unknown schema version", VersionedLoaderRejectsUnknownVersion),
    ("schema 1.1 loader rejects unmapped properties", Version11LoaderRejectsUnmappedProperty),
    ("loader rejects integer enum tokens", LoaderRejectsIntegerEnumTokens),
    ("schema 1.1 loader rejects missing required annotation scalars", Version11LoaderRejectsMissingRequiredScalars),
    ("null value evidence diagnostic fails validation", NullValueEvidenceDiagnosticFailsValidation),
    ("legacy broken and unsupported states parse audit and reserialize", LegacyAssociationStatesRoundTrip),
    ("schema 1.1 rejects unsupported association vocabulary", Version11RejectsUnsupportedAssociation),
    ("dash designator normalization handles NX dash variants", DashDesignatorNormalization),
    ("categorical port minus 12 versus minus 16 has no numeric difference", CategoricalPortMismatch),
    ("missing port mapping provenance is diagnostic only", MissingPortMappingDiagnostic),
    ("ambiguous port mapping is diagnostic only", AmbiguousPortMappingDiagnostic),
    ("null and blank port mapping candidates fail validation", InvalidPortMappingCandidateEntriesFailValidation),
    ("noncanonical and conflicting mapping candidates are diagnostic only", MappingCandidateSemanticConflictsAreDiagnosticOnly),
    ("numeric display resolution accepts 45.2378 shown as 45.24", NumericDisplayResolutionAllowsRounding),
    ("numeric adjacent wrong digit is a mismatch", NumericAdjacentWrongDigitMismatch),
    ("numeric midpoint boundary is inclusive", NumericMidpointBoundary),
    ("numeric unit mismatch is diagnostic only", NumericUnitMismatchDiagnostic),
    ("numeric signed and magnitude comparisons differ", NumericSignedAndMagnitudeComparison),
    ("manual override positive and negative cases", ManualOverrideCases),
    ("fully associated stale dimension is classified", AssociativeButStaleDimension),
    ("partially associated dimension suppresses mismatch", PartiallyAssociatedDimension),
    ("broken dimension suppresses mismatch", BrokenDimension),
    ("unsupported extraction is diagnostic only", UnsupportedExtraction),
    ("failed extraction is diagnostic only", FailedExtraction),
    ("complete unknown association is diagnostic only", CompleteUnknownAssociation),
    ("complete low confidence evidence is diagnostic only", CompleteLowConfidenceEvidence),
    ("null associated geometry JSON fails validation", NullAssociatedGeometryFailsValidation),
    ("NX modified-state drift fails validation", NxModifiedStateDriftFailsValidation),
    ("fractional scientific and dual numeric formats are diagnostic only", UnsupportedNumericDisplayFormats),
    ("legacy projection conflicts suppress dependent rules", ProjectionConflictsSuppressRules),
    ("automatic value conflict suppresses mismatch", AutomaticValueConflictSuppressesMismatch),
    ("global duplicate annotation ID fails validation", GlobalDuplicateAnnotationIdFailsValidation),
    ("inconsistent geometry owner link suppresses mismatch", InconsistentGeometryOwnerSuppressesMismatch),
    ("duplicate exact definition key ignores nominal values", DuplicateDefinitionIgnoresNominal),
    ("different characteristics on one feature are information", DifferentCharacteristicsAreInformation),
    ("missing definition identity suppresses duplicate conclusion", MissingDefinitionIdentity),
    ("multiple associated objects retain direct definition conclusion", MultipleAssociationsRetainDirectDefinition),
    ("integrated wrong left correct right duplicate and reference", IntegratedSameFeatureScenario),
    ("flatness without datums is accepted", FlatnessWithoutDatums),
    ("schema 1.1 flatness is diagnostic only pending evidence contract", Version11FlatnessIsDiagnosticOnly),
    ("second colleague scenario placeholder remains named", SecondColleagueScenarioPlaceholder),
    ("schema 1.1 JSON round trip preserves evidence", Version11JsonRoundTrip),
    ("HTML report contains required evidence fields", HtmlReportContainsEvidence)
};

var failures = new List<string>();
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {name}: {exception.Message}");
    }
}

foreach (var failure in failures)
{
    Console.Error.WriteLine(failure);
}

Console.WriteLine($"Regression summary: {tests.Length - failures.Count}/{tests.Length} passed.");
return failures.Count == 0 ? 0 : 1;

static void DrawingCadMismatch()
{
    var result = RunLegacy(View("RIGHT", Dimension("D1", "PORT-1", -12, -16)));
    AssertSingle(result, "DRAWING_CAD_VALUE_MISMATCH");
}

static void DuplicateHole()
{
    var result = RunLegacy(
        View("LEFT", Dimension("D-LEFT", "HOLE-1", 10, 10)),
        View("RIGHT", Dimension("D-RIGHT", "HOLE-1", 10, 10)));
    AssertSingle(result, "DUPLICATE_FEATURE_DEFINITION");
}

static void ViewSpecificMismatch()
{
    var result = RunLegacy(
        View("LEFT", Dimension("D-LEFT", "EDGE-LEFT", 12, 16)),
        View("RIGHT", Dimension("D-RIGHT", "EDGE-RIGHT", 16, 16)));
    var finding = AssertSingle(result, "DRAWING_CAD_VALUE_MISMATCH");
    Equal("D-LEFT", finding.AnnotationId, "Only the incorrect left-view annotation should fail.");
}

static void FlatnessWithDatums()
{
    var frame = new GdtFrame(GdtCharacteristic.Flatness, 0.1, ["A", "B"], []);
    var annotation = Annotation("FCF-1", AnnotationKind.FeatureControlFrame, "FLATNESS|0.1|A|B", null, null, false, "FACE-1", frame);
    var result = RunLegacy(View("TOP", annotation));
    AssertSingle(result, "FLATNESS_REFERENCES_DATUM");
}

static void ReferenceDimensionAllowed()
{
    var defining = Dimension("D1", "HOLE-1", 10, 10);
    var reference = Dimension("D2", "HOLE-1", 10, 10) with { IsReference = true };
    var result = RunLegacy(View("TOP", defining, reference));
    AssertNoFinding(result, "DUPLICATE_FEATURE_DEFINITION", "Reference dimension must not count as a second definition.");
}

static void VersionedLoaderAcceptsKnownVersions()
{
    var legacy = LoadFixture("examples", "four-defect-audit.json");
    var current = LoadFixture("examples", "port-dash-mismatch-1.1.json");
    Equal("1.0", legacy.SchemaVersion, "The canonical legacy fixture should load through the version dispatcher.");
    Equal("1.1", current.SchemaVersion, "The evidence-rich fixture should load through the version dispatcher.");
}

static void VersionedLoaderRejectsMissingVersion()
{
    AssertValidationCode(
        () => AuditDocumentLoader.Load("{\"drawing\":{}}"),
        "SCHEMA_VERSION_REQUIRED");
}

static void VersionedLoaderRejectsUnknownVersion()
{
    AssertValidationCode(
        () => AuditDocumentLoader.Load("{\"schemaVersion\":\"2.0\"}"),
        "SCHEMA_VERSION_UNSUPPORTED");
}

static void Version11LoaderRejectsUnmappedProperty()
{
    var json = File.ReadAllText(FindFixturePath("examples", "port-dash-mismatch-1.1.json"));
    var root = JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidOperationException("The schema 1.1 fixture did not parse as a JSON object.");
    root["unmappedPilotProperty"] = true;
    AssertThrows<JsonException>(
        () => AuditDocumentLoader.Load(root.ToJsonString()),
        "Schema 1.1 must reject unknown properties instead of silently discarding them.");
}

static void LoaderRejectsIntegerEnumTokens()
{
    var json = File.ReadAllText(FindFixturePath("examples", "legacy-association-states-1.0.json"));
    var root = JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidOperationException("The legacy fixture did not parse as a JSON object.");
    root["sheets"]![0]!["views"]![0]!["annotations"]![0]!["associationStatus"] = 2;
    AssertThrows<JsonException>(
        () => AuditDocumentLoader.Load(root.ToJsonString()),
        "Integer enum tokens must be rejected; only named string values are part of the wire contract.");
}

static void Version11LoaderRejectsMissingRequiredScalars()
{
    var json = File.ReadAllText(FindFixturePath("examples", "port-dash-mismatch-1.1.json"));
    foreach (var requiredProperty in new[] { "isReference", "kind" })
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("The schema 1.1 fixture did not parse as a JSON object.");
        var annotation = root["sheets"]![0]!["views"]![0]!["annotations"]![0]!.AsObject();
        True(annotation.Remove(requiredProperty), $"Fixture annotation should contain required property '{requiredProperty}'.");
        AssertThrows<JsonException>(
            () => AuditDocumentLoader.Load(root.ToJsonString()),
            $"Schema 1.1 must reject missing required scalar '{requiredProperty}' instead of applying a CLR default.");
    }
}

static void NullValueEvidenceDiagnosticFailsValidation()
{
    var json = File.ReadAllText(FindFixturePath("examples", "port-dash-mismatch-1.1.json"));
    var root = JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidOperationException("The schema 1.1 fixture did not parse as a JSON object.");
    var valueEvidence = root["sheets"]![0]!["views"]![0]!["annotations"]![0]!["valueEvidence"]!.AsObject();
    valueEvidence["diagnostics"] = new JsonArray((JsonNode?)null);
    AssertValidationCode(
        () => AuditDocumentLoader.Load(root.ToJsonString()),
        "DIAGNOSTIC_REQUIRED");
}

static void LegacyAssociationStatesRoundTrip()
{
    var document = LoadFixture("examples", "legacy-association-states-1.0.json");
    var annotations = document.Sheets.Single().Views.Single().Annotations;
    Equal(AssociationStatus.Broken, annotations[0].AssociationStatus, "Legacy broken must remain readable.");
    Equal(AssociationStatus.Unsupported, annotations[1].AssociationStatus, "Legacy unsupported must remain readable.");

    var result = AuditEngine.CreateDefault().Run(document);
    var broken = AssertSingle(result, "BROKEN_DIMENSION_ASSOCIATIVITY");
    Equal("DIM-BROKEN", broken.AnnotationId, "Only the broken legacy annotation should produce the associativity finding.");

    var unsupportedFacts = EffectiveAnnotationFactsResolver.Resolve(document, annotations[1]);
    Equal(AssociationStatus.Unknown, unsupportedFacts.AssociationStatus, "Legacy unsupported should resolve to unknown association.");
    Equal(ExtractionState.Unsupported, unsupportedFacts.ExtractionState, "Legacy unsupported should resolve to unsupported extraction.");

    var serialized = AuditJson.Serialize(document);
    Contains(serialized, "\"associationStatus\": \"broken\"", "Legacy broken wire name must be preserved.");
    Contains(serialized, "\"associationStatus\": \"unsupported\"", "Legacy unsupported wire name must be preserved.");
    var reloaded = AuditDocumentLoader.Load(serialized);
    Equal(AssociationStatus.Unsupported, reloaded.Sheets[0].Views[0].Annotations[1].AssociationStatus, "Legacy unsupported must survive a round trip.");
}

static void Version11RejectsUnsupportedAssociation()
{
    var annotation = NumericEvidence(
        "DIM-UNSUPPORTED-WIRE",
        "FEATURE-U",
        "length",
        "EDGE-U",
        10,
        10,
        10,
        evidenceAssociation: AssociationStatus.Unsupported,
        extractionState: ExtractionState.Unsupported) with
    {
        AssociationStatus = AssociationStatus.Unsupported
    };
    var document = Document11(View11("FRONT", annotation));
    AssertValidationCode(
        () => AuditDocumentValidator.ValidateAndThrow(document),
        "ASSOCIATION_UNSUPPORTED_LEGACY_ONLY");
}

static void DashDesignatorNormalization()
{
    foreach (var input in new[] { "-12", " − 12 ", "–12", "— 0012", "－12" })
    {
        Equal("-12", AnnotationValueComparer.NormalizeDashDesignator(input), $"Dash form '{input}' should normalize.");
    }

    Equal<string?>(null, AnnotationValueComparer.NormalizeDashDesignator("12"), "A designation must contain a leading dash.");
    Equal<string?>(null, AnnotationValueComparer.NormalizeDashDesignator("-12A"), "A designation suffix must not be guessed.");
}

static void CategoricalPortMismatch()
{
    var document = LoadFixture("examples", "port-dash-mismatch-1.1.json");
    var result = AuditEngine.CreateDefault().Run(document);
    var finding = AssertSingle(result, "DRAWING_CAD_VALUE_MISMATCH");
    Equal("-12", finding.Observed, "Displayed designation should be canonicalized.");
    Equal("-16", finding.Expected, "CAD designation should be canonicalized.");
    Equal("not applicable", finding.Difference, "Categorical designations must not report arithmetic difference.");
    Contains(finding.Message, "numeric difference is not applicable", "The finding must explain categorical comparison.");
    DoesNotContain(finding.Message, "difference 4", "The port finding must not claim a numeric difference of four.");
}

static void MissingPortMappingDiagnostic()
{
    var annotation = DashEvidence(
        "PORT-MAPPING-MISSING",
        "PORT-M1",
        "−12",
        "-12",
        "-12",
        "-16",
        EvidenceAuthority.ApprovedLocalMapping,
        ExtractionState.Incomplete,
        portFamily: null,
        mappingRevision: null,
        candidateCadDesignations: ["-16"]);
    var result = Run11(View11("RIGHT", annotation));
    AssertDiagnostic(result, "PORT_MAPPING_PROVENANCE_MISSING", "PORT-MAPPING-MISSING");
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Missing mapping provenance must suppress a mismatch conclusion.");
}

static void AmbiguousPortMappingDiagnostic()
{
    var annotation = DashEvidence(
        "PORT-MAPPING-AMBIGUOUS",
        "PORT-M2",
        "-12",
        "-12",
        "-12",
        "-16",
        EvidenceAuthority.ApprovedLocalMapping,
        ExtractionState.Incomplete,
        portFamily: "PORT-FAMILY-X",
        mappingRevision: "MAP-REV-A",
        candidateCadDesignations: ["-16", "-20"]);
    var result = Run11(View11("RIGHT", annotation));
    AssertDiagnostic(result, "PORT_DESIGNATION_AMBIGUOUS", "PORT-MAPPING-AMBIGUOUS");
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Ambiguous mapping must suppress a mismatch conclusion.");
}

static void InvalidPortMappingCandidateEntriesFailValidation()
{
    var json = File.ReadAllText(FindFixturePath("examples", "port-dash-mismatch-1.1.json"));
    foreach (JsonNode? invalidCandidate in new JsonNode?[] { null, JsonValue.Create("   ") })
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("The schema 1.1 fixture did not parse as a JSON object.");
        var valueEvidence = root["sheets"]![0]!["views"]![0]!["annotations"]![0]!["valueEvidence"]!.AsObject();
        valueEvidence["candidateCadDesignations"] = new JsonArray(invalidCandidate?.DeepClone());
        AssertValidationCode(
            () => AuditDocumentLoader.Load(root.ToJsonString()),
            "PORT_DESIGNATION_CANDIDATE_INVALID");
    }
}

static void MappingCandidateSemanticConflictsAreDiagnosticOnly()
{
    var noncanonical = DashEvidence(
        "PORT-CANDIDATE-NONCANONICAL",
        "PORT-FEATURE-NONCANONICAL",
        "-12",
        "-12",
        "-12",
        "-16",
        EvidenceAuthority.ApprovedLocalMapping,
        ExtractionState.Complete,
        portFamily: "PORT-FAMILY-X",
        mappingRevision: "MAP-REV-A",
        candidateCadDesignations: ["SIZE-16"]);
    var conflicting = DashEvidence(
        "PORT-CANDIDATE-CONFLICT",
        "PORT-FEATURE-CONFLICT",
        "-12",
        "-12",
        "-12",
        "-16",
        EvidenceAuthority.ApprovedLocalMapping,
        ExtractionState.Complete,
        portFamily: "PORT-FAMILY-X",
        mappingRevision: "MAP-REV-A",
        candidateCadDesignations: ["-20"]);
    var result = Run11(View11("RIGHT", noncanonical, conflicting));
    AssertDiagnostic(result, "PORT_DESIGNATION_CANDIDATE_INVALID", noncanonical.Id);
    AssertDiagnostic(result, "PORT_MAPPING_RESULT_CONFLICT", conflicting.Id);
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Invalid or CAD-disagreeing mapping candidates must suppress mismatch conclusions.");
}

static void NumericDisplayResolutionAllowsRounding()
{
    var annotation = NumericEvidence(
        "DIM-ROUNDING",
        "FEATURE-R",
        "linear.length",
        "EDGE-R",
        45.24,
        45.2378,
        45.2378,
        displayResolution: 0.01);
    var result = Run11(View11("FRONT", annotation));
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "A correctly rounded displayed value must not be flagged.");
}

static void NumericAdjacentWrongDigitMismatch()
{
    var annotation = NumericEvidence(
        "DIM-WRONG-DIGIT",
        "FEATURE-W",
        "linear.length",
        "EDGE-W",
        45.25,
        45.25,
        45.2378,
        displayResolution: 0.01);
    var finding = AssertSingle(Run11(View11("FRONT", annotation)), "DRAWING_CAD_VALUE_MISMATCH");
    Equal("DIM-WRONG-DIGIT", finding.AnnotationId, "The adjacent wrong digit must be identified.");
}

static void NumericMidpointBoundary()
{
    var midpoint = NumericEvidence(
        "DIM-MIDPOINT",
        "FEATURE-MID",
        "linear.length",
        "EDGE-MID",
        100,
        100,
        100.005,
        displayResolution: 0.01);
    var beyond = NumericEvidence(
        "DIM-BEYOND-MIDPOINT",
        "FEATURE-BEYOND",
        "linear.length",
        "EDGE-BEYOND",
        100,
        100,
        100.0051,
        displayResolution: 0.01);
    var result = Run11(View11("FRONT", midpoint, beyond));
    var mismatch = AssertSingle(result, "DRAWING_CAD_VALUE_MISMATCH");
    Equal("DIM-BEYOND-MIDPOINT", mismatch.AnnotationId, "Half-resolution is inclusive; a value just beyond it must fail.");
}

static void NumericUnitMismatchDiagnostic()
{
    var annotation = NumericEvidence(
        "DIM-UNIT-MISMATCH",
        "FEATURE-UNIT",
        "linear.length",
        "EDGE-UNIT",
        1,
        1,
        25.4,
        units: "in",
        cadUnits: "mm",
        displayResolution: 0.01);
    var result = Run11(View11("FRONT", annotation));
    AssertDiagnostic(result, "NUMERIC_UNITS_INCOMPATIBLE", "DIM-UNIT-MISMATCH");
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Unnormalized units must not produce an engineering mismatch.");
}

static void NumericSignedAndMagnitudeComparison()
{
    var magnitude = NumericEvidence(
        "DIM-MAGNITUDE",
        "FEATURE-MAG",
        "diameter",
        "EDGE-MAG",
        -12,
        -12,
        12,
        comparisonMode: NumericComparisonMode.Magnitude);
    var signed = NumericEvidence(
        "DIM-SIGNED",
        "FEATURE-SIGNED",
        "coordinate.x",
        "POINT-SIGNED",
        -12,
        -12,
        12,
        comparisonMode: NumericComparisonMode.Signed);
    var result = Run11(View11("FRONT", magnitude, signed));
    var mismatch = AssertSingle(result, "DRAWING_CAD_VALUE_MISMATCH");
    Equal("DIM-SIGNED", mismatch.AnnotationId, "Only signed-coordinate comparison should distinguish -12 from +12.");
}

static void ManualOverrideCases()
{
    var overridden = NumericEvidence(
        "DIM-OVERRIDDEN",
        "FEATURE-OVERRIDE",
        "linear.length",
        "EDGE-OVERRIDE",
        10,
        12,
        12,
        manualOverrideState: ManualOverrideState.Overridden);
    var automatic = NumericEvidence(
        "DIM-AUTOMATIC",
        "FEATURE-AUTOMATIC",
        "linear.length",
        "EDGE-AUTOMATIC",
        12,
        12,
        12);
    var result = Run11(View11("FRONT", overridden, automatic));
    var overrideFinding = AssertSingle(result, "MANUAL_DIMENSION_OVERRIDE");
    Equal("DIM-OVERRIDDEN", overrideFinding.AnnotationId, "Only the directly reported override should flag.");
    Equal(AnnotationAssessment.ManualOverride, overrideFinding.Assessment, "Override assessment should be explicit.");
    var mismatch = AssertSingle(result, "DRAWING_CAD_VALUE_MISMATCH");
    Equal("DIM-OVERRIDDEN", mismatch.AnnotationId, "Complete CAD evidence should still evaluate the overridden value.");
}

static void AssociativeButStaleDimension()
{
    var annotation = NumericEvidence(
        "DIM-STALE",
        "FEATURE-STALE",
        "linear.length",
        "EDGE-STALE",
        10,
        10,
        12);
    var finding = AssertSingle(Run11(View11("FRONT", annotation)), "DRAWING_CAD_VALUE_MISMATCH");
    Equal(AnnotationAssessment.AssociativeButStale, finding.Assessment, "Displayed=automatic but automatic!=CAD should classify as stale.");
}

static void PartiallyAssociatedDimension()
{
    var annotation = NumericEvidence(
        "DIM-PARTIAL",
        "FEATURE-PARTIAL",
        "linear.length",
        "EDGE-PARTIAL",
        10,
        10,
        12,
        evidenceAssociation: AssociationStatus.PartiallyAssociated);
    var result = Run11(View11("FRONT", annotation));
    var finding = AssertSingle(result, "BROKEN_DIMENSION_ASSOCIATIVITY");
    Equal(AnnotationAssessment.PartiallyAssociated, finding.Assessment, "Partial association assessment should be explicit.");
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Partial association must suppress mismatch.");
}

static void BrokenDimension()
{
    var annotation = NumericEvidence(
        "DIM-BROKEN-11",
        "FEATURE-BROKEN",
        "linear.length",
        "EDGE-BROKEN",
        10,
        10,
        12,
        evidenceAssociation: AssociationStatus.Broken);
    var result = Run11(View11("FRONT", annotation));
    var finding = AssertSingle(result, "BROKEN_DIMENSION_ASSOCIATIVITY");
    Equal(AnnotationAssessment.BrokenAssociation, finding.Assessment, "Broken association assessment should be explicit.");
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Broken association must suppress mismatch.");
}

static void UnsupportedExtraction()
{
    var annotation = NumericEvidence(
        "DIM-EXTRACTION-UNSUPPORTED",
        "FEATURE-UNSUPPORTED",
        "linear.length",
        "EDGE-UNSUPPORTED",
        10,
        null,
        null,
        manualOverrideState: ManualOverrideState.Unknown,
        evidenceAssociation: AssociationStatus.Unknown,
        extractionState: ExtractionState.Unsupported);
    var result = Run11(View11("FRONT", annotation));
    AssertDiagnostic(result, "ANNOTATION_EXTRACTION_UNSUPPORTED", annotation.Id);
    Equal(0, result.Findings.Count, "Unsupported extraction must not create an engineering finding.");
}

static void FailedExtraction()
{
    var annotation = NumericEvidence(
        "DIM-EXTRACTION-FAILED",
        "FEATURE-FAILED",
        "linear.length",
        "EDGE-FAILED",
        10,
        null,
        null,
        manualOverrideState: ManualOverrideState.Unknown,
        evidenceAssociation: AssociationStatus.Unknown,
        extractionState: ExtractionState.Failed);
    var result = Run11(View11("FRONT", annotation));
    AssertDiagnostic(result, "ANNOTATION_EXTRACTION_FAILED", annotation.Id);
    Equal(0, result.Findings.Count, "Failed extraction must not create an engineering finding.");
}

static void CompleteUnknownAssociation()
{
    var annotation = NumericEvidence(
        "DIM-ASSOCIATION-UNKNOWN",
        "FEATURE-ASSOCIATION-UNKNOWN",
        "linear.length",
        "EDGE-ASSOCIATION-UNKNOWN",
        10,
        10,
        12,
        evidenceAssociation: AssociationStatus.Unknown);
    var result = Run11(View11("FRONT", annotation));
    AssertDiagnostic(result, "ASSOCIATION_STATE_UNKNOWN", annotation.Id);
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Unknown association must suppress mismatch even when extraction is marked complete.");
}

static void CompleteLowConfidenceEvidence()
{
    var annotation = NumericEvidence(
        "DIM-LOW-CONFIDENCE",
        "FEATURE-LOW-CONFIDENCE",
        "linear.length",
        "EDGE-LOW-CONFIDENCE",
        10,
        10,
        12,
        confidence: EvidenceConfidence.Low);
    var result = Run11(View11("FRONT", annotation));
    AssertDiagnostic(result, "EVIDENCE_CONFIDENCE_INSUFFICIENT", annotation.Id);
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Low-confidence evidence must suppress mismatch.");
}

static void NullAssociatedGeometryFailsValidation()
{
    var json = File.ReadAllText(FindFixturePath("examples", "port-dash-mismatch-1.1.json"));
    var root = JsonNode.Parse(json)?.AsObject()
        ?? throw new InvalidOperationException("The schema 1.1 fixture did not parse as a JSON object.");
    root["sheets"]![0]!["views"]![0]!["annotations"]![0]!["valueEvidence"]!["associatedGeometry"] = null;
    AssertValidationCode(
        () => AuditDocumentLoader.Load(root.ToJsonString()),
        "ASSOCIATED_GEOMETRY_ARRAY_REQUIRED");
}

static void NxModifiedStateDriftFailsValidation()
{
    var baseline = Document11(View11(
        "FRONT",
        NumericEvidence("DIM-MODIFIED-STATE", "FEATURE-MODIFIED-STATE", "linear.length", "EDGE-MODIFIED-STATE", 10, 10, 10)));
    var metadata = baseline.ExtractionMetadata
        ?? throw new InvalidOperationException("Schema 1.1 regression document is missing extraction metadata.");
    foreach (var changedMetadata in new[]
             {
                 metadata with { DrawingModifiedAfter = !metadata.DrawingModifiedBefore },
                 metadata with { ReferencedModelModifiedAfter = !metadata.ReferencedModelModifiedBefore }
             })
    {
        var changed = baseline with { ExtractionMetadata = changedMetadata };
        AssertValidationCode(
            () => AuditDocumentValidator.ValidateAndThrow(changed),
            "NX_MODIFIED_STATE_CHANGED");
    }
}

static void UnsupportedNumericDisplayFormats()
{
    var fraction = NumericEvidence(
        "DIM-FRACTION",
        "FEATURE-FRACTION",
        "linear.length",
        "EDGE-FRACTION",
        0.5,
        0.5,
        1,
        rawDisplayedText: "1/2");
    var scientific = NumericEvidence(
        "DIM-SCIENTIFIC",
        "FEATURE-SCIENTIFIC",
        "linear.length",
        "EDGE-SCIENTIFIC",
        10,
        10,
        12,
        rawDisplayedText: "1e1");
    var dual = NumericEvidence(
        "DIM-DUAL-UNIT",
        "FEATURE-DUAL-UNIT",
        "linear.length",
        "EDGE-DUAL-UNIT",
        10,
        10,
        12,
        rawDisplayedText: "10 mm / 0.394 in");
    var result = Run11(View11("FRONT", fraction, scientific, dual));
    AssertDiagnostic(result, "NUMERIC_DISPLAY_FORMAT_UNSUPPORTED", fraction.Id);
    AssertDiagnostic(result, "NUMERIC_DISPLAY_FORMAT_UNSUPPORTED", scientific.Id);
    AssertDiagnostic(result, "NUMERIC_DISPLAY_FORMAT_UNSUPPORTED", dual.Id);
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Unsupported display formats must not be reduced to a parsed numeric mismatch.");
}

static void ProjectionConflictsSuppressRules()
{
    var displayedConflict = NumericEvidence(
        "DIM-DISPLAY-CONFLICT",
        "FEATURE-DISPLAY-CONFLICT",
        "linear.length",
        "EDGE-DC",
        10,
        10,
        12) with
    {
        DisplayedValue = 11
    };
    var overrideConflict = NumericEvidence(
        "DIM-OVERRIDE-CONFLICT",
        "FEATURE-OVERRIDE-CONFLICT",
        "linear.length",
        "EDGE-OC",
        10,
        12,
        12,
        manualOverrideState: ManualOverrideState.Overridden) with
    {
        IsManualOverride = false
    };
    var associationConflict = NumericEvidence(
        "DIM-ASSOCIATION-CONFLICT",
        "FEATURE-ASSOCIATION-CONFLICT",
        "linear.length",
        "EDGE-AC",
        10,
        10,
        12,
        evidenceAssociation: AssociationStatus.PartiallyAssociated) with
    {
        AssociationStatus = AssociationStatus.Broken
    };

    var result = Run11(View11("FRONT", displayedConflict, overrideConflict, associationConflict));
    AssertDiagnostic(result, "EVIDENCE_CONFLICT_DISPLAYED_VALUE", displayedConflict.Id);
    AssertDiagnostic(result, "EVIDENCE_CONFLICT_OVERRIDE_STATE", overrideConflict.Id);
    AssertDiagnostic(result, "EVIDENCE_CONFLICT_ASSOCIATION_STATE", associationConflict.Id);
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Conflicting value/override/association evidence must suppress mismatch conclusions.");
    AssertNoFinding(result, "MANUAL_DIMENSION_OVERRIDE", "Conflicting override evidence must suppress the override conclusion.");
    AssertNoFinding(result, "BROKEN_DIMENSION_ASSOCIATIVITY", "Conflicting association evidence must suppress associativity conclusions.");
}

static void AutomaticValueConflictSuppressesMismatch()
{
    var annotation = NumericEvidence(
        "DIM-AUTOMATIC-CONFLICT",
        "FEATURE-AUTO-CONFLICT",
        "linear.length",
        "EDGE-AUTO-CONFLICT",
        10,
        11,
        12);
    var result = Run11(View11("FRONT", annotation));
    AssertDiagnostic(result, "EVIDENCE_CONFLICT_AUTOMATIC_VALUE", annotation.Id);
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "Contradictory automatic evidence must suppress cause classification and mismatch.");
}

static void GlobalDuplicateAnnotationIdFailsValidation()
{
    var first = NumericEvidence("DIM-GLOBAL-DUPLICATE", "FEATURE-GLOBAL-1", "linear.length", "EDGE-GLOBAL-1", 10, 10, 10);
    var second = NumericEvidence("DIM-GLOBAL-DUPLICATE", "FEATURE-GLOBAL-2", "linear.length", "EDGE-GLOBAL-2", 20, 20, 20);
    var document = Document11(View11("LEFT", first), View11("RIGHT", second));
    AssertValidationCode(
        () => AuditDocumentValidator.ValidateAndThrow(document),
        "ANNOTATION_ID_DUPLICATE");
}

static void InconsistentGeometryOwnerSuppressesMismatch()
{
    var original = NumericEvidence(
        "DIM-BAD-OWNER",
        "FEATURE-BAD-OWNER",
        "linear.length",
        "EDGE-BAD-OWNER",
        10,
        10,
        12);
    var evidence = original.ValueEvidence
        ?? throw new InvalidOperationException("Regression helper did not create value evidence.");
    var badGeometry = evidence.AssociatedGeometry
        .Select(reference => reference with { DirectOwnerEvidenceIds = ["FEATURE-NOT-THE-OWNER"] })
        .ToArray();
    var annotation = original with
    {
        ValueEvidence = evidence with { AssociatedGeometry = badGeometry }
    };
    var result = Run11(View11("FRONT", annotation));
    AssertDiagnostic(result, "EVIDENCE_CONFLICT_DEFINITION_IDENTITY", annotation.Id);
    AssertNoFinding(result, "DRAWING_CAD_VALUE_MISMATCH", "An inconsistent direct owner link must suppress mismatch.");
}

static void DuplicateDefinitionIgnoresNominal()
{
    var first = NumericEvidence("DIM-DUP-10", "FEATURE-DUP", "hole.diameter", "CYLINDER-DUP", 10, 10, 10);
    var second = NumericEvidence("DIM-DUP-12", "FEATURE-DUP", "hole.diameter", "CYLINDER-DUP", 12, 12, 12);
    var finding = AssertSingle(
        Run11(View11("LEFT", first), View11("RIGHT", second)),
        "DUPLICATE_FEATURE_DEFINITION");
    Equal(FindingSeverity.Error, finding.Severity, "Same feature/characteristic/target must be an error even when nominal values differ.");
}

static void DifferentCharacteristicsAreInformation()
{
    var diameter = NumericEvidence("DIM-DIAMETER", "FEATURE-HOLE", "hole.diameter", "CYLINDER-HOLE", 10, 10, 10);
    var depth = NumericEvidence("DIM-DEPTH", "FEATURE-HOLE", "hole.depth", "BOTTOM-HOLE", 20, 20, 20);
    var result = Run11(View11("FRONT", diameter, depth));
    var advisories = result.Findings.Where(finding => finding.RuleId == "DUPLICATE_FEATURE_DEFINITION").ToArray();
    Equal(1, advisories.Length, "Different characteristics should produce one review advisory.");
    Equal(FindingSeverity.Information, advisories[0].Severity, "Different characteristics must not be a duplicate error.");
    Equal(FindingSeverity.Information, advisories[0].Metadata?.Severity, "Finding-specific rule metadata must agree with information severity.");
}

static void MissingDefinitionIdentity()
{
    var first = NumericEvidence(
        "DIM-MISSING-ID-1",
        "FEATURE-MISSING-ID",
        "hole.diameter",
        "CYLINDER-MISSING-ID",
        10,
        10,
        10,
        includeDefinitionIdentity: false);
    var second = NumericEvidence(
        "DIM-MISSING-ID-2",
        "FEATURE-MISSING-ID",
        "hole.diameter",
        "CYLINDER-MISSING-ID",
        10,
        10,
        10,
        includeDefinitionIdentity: false);
    var result = Run11(View11("LEFT", first), View11("RIGHT", second));
    AssertDiagnostic(result, "DEFINITION_IDENTITY_INCOMPLETE", first.Id);
    AssertDiagnostic(result, "DEFINITION_IDENTITY_INCOMPLETE", second.Id);
    AssertNoFinding(result, "DUPLICATE_FEATURE_DEFINITION", "Missing direct identity must suppress duplicate conclusions.");
}

static void MultipleAssociationsRetainDirectDefinition()
{
    var first = NumericEvidence(
        "DIM-MULTI-1",
        "FEATURE-MULTI",
        "hole.diameter",
        "CYLINDER-MULTI",
        10,
        10,
        10,
        legacyFeatureIds: ["FEATURE-MULTI", "FEATURE-OTHER"]);
    var second = NumericEvidence(
        "DIM-MULTI-2",
        "FEATURE-MULTI",
        "hole.diameter",
        "CYLINDER-MULTI",
        10,
        10,
        10,
        legacyFeatureIds: ["FEATURE-MULTI", "FEATURE-OTHER"]);
    var result = Run11(View11("LEFT", first), View11("RIGHT", second));
    var duplicate = AssertSingle(result, "DUPLICATE_FEATURE_DEFINITION");
    Equal(FindingSeverity.Error, duplicate.Severity, "A direct DefinitionIdentity may conclude despite extra associated objects.");
    AssertNoDiagnostic(result, "EVIDENCE_CONFLICT_DEFINITION_IDENTITY", first.Id);
    AssertNoDiagnostic(result, "EVIDENCE_CONFLICT_DEFINITION_IDENTITY", second.Id);
}

static void IntegratedSameFeatureScenario()
{
    var wrongLeft = NumericEvidence("DIM-HOLE-LEFT-11", "FEATURE-INTEGRATED", "hole.diameter", "CYLINDER-INTEGRATED", 10, 10, 12);
    var correctRight = NumericEvidence("DIM-HOLE-RIGHT-11", "FEATURE-INTEGRATED", "hole.diameter", "CYLINDER-INTEGRATED", 12, 12, 12);
    var reference = NumericEvidence(
        "DIM-HOLE-REFERENCE-11",
        "FEATURE-INTEGRATED",
        "hole.diameter",
        "CYLINDER-INTEGRATED",
        12,
        12,
        12,
        isReference: true);

    var result = Run11(
        View11("LEFT", wrongLeft),
        View11("RIGHT", correctRight),
        View11("TOP", reference));
    var mismatch = AssertSingle(result, "DRAWING_CAD_VALUE_MISMATCH");
    Equal(wrongLeft.Id, mismatch.AnnotationId, "Only the wrong left annotation should mismatch.");
    var duplicate = AssertSingle(result, "DUPLICATE_FEATURE_DEFINITION");
    Equal("2", duplicate.Observed, "Only the two non-reference definitions should count as duplicates.");
}

static void FlatnessWithoutDatums()
{
    var allowedFlatness = Annotation(
        "FCF-FLATNESS-ALLOWED",
        AnnotationKind.FeatureControlFrame,
        "FLATNESS|0.1",
        null,
        null,
        false,
        "FACE-ALLOWED",
        new GdtFrame(GdtCharacteristic.Flatness, 0.1, [], []));
    var positionWithDatum = Annotation(
        "FCF-POSITION",
        AnnotationKind.FeatureControlFrame,
        "POSITION|0.1|A",
        null,
        null,
        false,
        "HOLE-POSITION",
        new GdtFrame(GdtCharacteristic.Position, 0.1, ["A"], []));
    var result = RunLegacy(View("TOP", allowedFlatness, positionWithDatum));
    AssertNoFinding(result, "FLATNESS_REFERENCES_DATUM", "Flatness without a datum and non-flatness controls with datums are permitted by this rule.");
}

static void Version11FlatnessIsDiagnosticOnly()
{
    var frame = new GdtFrame(GdtCharacteristic.Flatness, 0.1, ["A", "B"], []);
    var incomplete = Annotation(
        "FCF-11-INCOMPLETE",
        AnnotationKind.FeatureControlFrame,
        "FLATNESS|0.1|A|B",
        null,
        null,
        false,
        "FACE-FLATNESS-INCOMPLETE",
        frame);
    var complete = Annotation(
        "FCF-11-COMPLETE",
        AnnotationKind.FeatureControlFrame,
        "FLATNESS|0.1|A|B",
        null,
        null,
        false,
        "FACE-FLATNESS-COMPLETE",
        frame) with
    {
        AnnotationObject = ObjectReference(
            "EVIDENCE:FCF-11-COMPLETE",
            "88001",
            "NXOpen.Annotations.FeatureControlFrame")
    };
    var result = Run11(View11("TOP", incomplete, complete));
    AssertDiagnostic(result, "GDT_EXTRACTION_EVIDENCE_UNSUPPORTED", incomplete.Id);
    AssertDiagnostic(result, "GDT_EXTRACTION_EVIDENCE_UNSUPPORTED", complete.Id);
    AssertNoFinding(
        result,
        "FLATNESS_REFERENCES_DATUM",
        "Schema 1.1 FCFs must remain diagnostic-only until parse state, confidence, and provenance are explicit in the contract.");
}

static void SecondColleagueScenarioPlaceholder()
{
    const string pendingFixtureId = "COLLEAGUE-EXAMPLE-2-PENDING-DESCRIPTION";
    var placeholder = new AuditDocument(
        "1.0",
        new DrawingIdentity(pendingFixtureId, "PENDING", "unknown", "PENDING-COMPANY-INTERPRETATION"),
        [new DrawingSheet("S-PENDING", "PENDING", [])],
        []);
    Equal(pendingFixtureId, placeholder.Drawing.PartNumber, "The undescribed colleague scenario must retain a named fixture placeholder.");
    Equal(0, AuditEngine.CreateDefault().Run(placeholder).Findings.Count, "No rule behavior may be invented before the scenario is described.");
}

static void Version11JsonRoundTrip()
{
    var original = Document11(View11(
        "RIGHT",
        DashEvidence("PORT-ROUNDTRIP", "PORT-RT", "− 12", "-12", "-12", "-16")));
    var json = AuditJson.Serialize(original);
    var roundTripped = AuditDocumentLoader.Load(json);
    Equal("1.1", roundTripped.SchemaVersion, "Schema version should survive serialization.");
    Equal(original.ExtractionMetadata, roundTripped.ExtractionMetadata, "Extraction metadata should survive serialization.");
    var evidence = roundTripped.Sheets[0].Views[0].Annotations[0].ValueEvidence;
    True(evidence is not null, "Value evidence should survive serialization.");
    Equal(SemanticValueKind.DashDesignator, evidence!.ValueKind, "Semantic value kind should survive serialization.");
    Equal("-16", evidence.CadDesignation, "CAD designation should survive serialization.");
    Equal(EvidenceConfidence.High, evidence.Confidence, "Evidence confidence should survive serialization.");
}

static void HtmlReportContainsEvidence()
{
    var annotation = DashEvidence(
        "PORT-CALLOUT-HTML",
        "PORT-FEATURE-HTML",
        "− 12",
        "-12",
        "-12",
        "-16",
        EvidenceAuthority.ApprovedLocalMapping,
        ExtractionState.Complete,
        portFamily: "PORT-FAMILY-HTML",
        mappingRevision: "MAP-REV-HTML",
        candidateCadDesignations: ["-16"]);
    var result = Run11(View11("RIGHT", annotation));
    var html = AuditHtmlRenderer.Render(result);
    foreach (var expected in new[]
             {
                 "Drafting-review advisory",
                 "SHEET 1",
                 "RIGHT",
                 "PORT-CALLOUT-HTML",
                 "− 12",
                 "-12",
                 "-16",
                 "not applicable",
                 "AssociativeButStale",
                 "High",
                 "ApprovedLocalMapping",
                 "PORT-FAMILY-HTML",
                 "MAP-REV-HTML",
                 "DRAWING_CAD_VALUE_MISMATCH",
                 "Operational diagnostics",
                 "NX modified-state evidence",
                 "Drawing before False / after False",
                 "Referenced model before False / after False",
                 "TEST-PART-11 rev A"
             })
    {
        Contains(html, expected, $"HTML report should contain '{expected}'.");
    }

    DoesNotContain(html, "difference 4", "HTML must not turn dash designations into arithmetic.");
}

static AuditResult RunLegacy(params DrawingView[] views) =>
    AuditEngine.CreateDefault().Run(new AuditDocument(
        "1.0",
        new DrawingIdentity("TEST-PART", "A", "mm", "ASME-Y14.100-2013/Y14.5-2009"),
        [new DrawingSheet("S1", "SHEET 1", views)],
        []));

static AuditResult Run11(params DrawingView[] views) =>
    AuditEngine.CreateDefault().Run(Document11(views));

static AuditDocument Document11(params DrawingView[] views)
{
    var features = views
        .SelectMany(view => view.Annotations)
        .Select(annotation => annotation.ValueEvidence?.DefinedFeature)
        .Where(reference => reference is not null)
        .Select(reference => reference!)
        .DistinctBy(reference => reference.EvidenceId, StringComparer.Ordinal)
        .Select(reference => new CadFeature(
            reference.EvidenceId,
            reference.NxTag,
            reference.ObjectType,
            reference.OwningPart)
        {
            ObjectReference = reference
        })
        .ToArray();

    return new AuditDocument(
        "1.1",
        new DrawingIdentity("TEST-PART-11", "A", "mm", "LOCAL-PILOT-PENDING-APPROVAL"),
        [new DrawingSheet("S1", "SHEET 1", views)],
        features)
    {
        ExtractionMetadata = new ExtractionMetadata
        {
            SourceSystem = "Regression fixture",
            ExtractorVersion = "1.1-test",
            RunId = "RUN-REGRESSION-001",
            ExtractedAtUtc = new DateTimeOffset(2026, 8, 20, 20, 0, 0, TimeSpan.Zero),
            NxRelease = "fixture-only",
            DrawingModifiedBefore = false,
            DrawingModifiedAfter = false,
            ReferencedModelModifiedBefore = false,
            ReferencedModelModifiedAfter = false
        }
    };
}

static DrawingView View(string name, params DrawingAnnotation[] annotations) =>
    new($"VIEW-{name}", name, "TEST-PART.prt", 1.0, annotations);

static DrawingView View11(string name, params DrawingAnnotation[] annotations) =>
    new($"VIEW-{name}", name, "TEST-PART-11.prt", 1.0, annotations)
    {
        EvidenceId = $"TEST-PART-11:A:view:{name}",
        Orientation = name,
        Transform = new DrawingViewTransform
        {
            Matrix = [1, 0, 0, 0, 1, 0, 0, 0, 1],
            Translation = [0, 0, 0]
        },
        ReferencedPartIdentity = ObjectReference("TEST-PART-11:A:part:1", "1", "NXOpen.Part")
    };

static DrawingAnnotation Dimension(string id, string featureId, double displayed, double measured) =>
    Annotation(id, AnnotationKind.Dimension, displayed.ToString(CultureInfo.InvariantCulture), displayed, measured, false, featureId, null);

static DrawingAnnotation Annotation(
    string id,
    AnnotationKind kind,
    string text,
    double? displayed,
    double? measured,
    bool isReference,
    string featureId,
    GdtFrame? frame) =>
    new(
        id,
        kind,
        text,
        displayed,
        measured,
        0.000001,
        isReference,
        false,
        AssociationStatus.Associated,
        [featureId],
        frame,
        new EvidenceLocation("S1", "VIEW", null, null));

static DrawingAnnotation NumericEvidence(
    string id,
    string featureId,
    string characteristicKey,
    string targetSubgeometryId,
    double displayed,
    double? automatic,
    double? cad,
    NumericComparisonMode comparisonMode = NumericComparisonMode.Signed,
    double? displayResolution = 0.01,
    string units = "mm",
    string cadUnits = "mm",
    ManualOverrideState manualOverrideState = ManualOverrideState.NotOverridden,
    AssociationStatus evidenceAssociation = AssociationStatus.Associated,
    ExtractionState extractionState = ExtractionState.Complete,
    EvidenceConfidence confidence = EvidenceConfidence.High,
    EvidenceAuthority authority = EvidenceAuthority.DirectNx,
    bool isReference = false,
    bool includeDefinitionIdentity = true,
    IReadOnlyList<string>? legacyFeatureIds = null,
    string? rawDisplayedText = null)
{
    var definedFeature = ObjectReference(featureId, TagFrom(featureId, 2), "NXOpen.Features.Feature");
    var annotationObject = ObjectReference($"EVIDENCE:{id}", TagFrom(id, 1), "NXOpen.Annotations.Dimension");
    var geometry = ObjectReference(targetSubgeometryId, TagFrom(targetSubgeometryId, 3), "NXOpen.Edge", featureId);
    var text = rawDisplayedText ?? displayed.ToString("G17", CultureInfo.InvariantCulture);
    var valueKind = comparisonMode == NumericComparisonMode.Magnitude
        ? SemanticValueKind.NumericMagnitude
        : SemanticValueKind.SignedCoordinate;

    return new DrawingAnnotation(
        id,
        AnnotationKind.Dimension,
        text,
        displayed,
        cad,
        0.000001,
        isReference,
        manualOverrideState == ManualOverrideState.Overridden,
        evidenceAssociation,
        legacyFeatureIds ?? [featureId],
        null,
        new EvidenceLocation("S1", "VIEW", 10, 20))
    {
        AnnotationObject = annotationObject,
        DefinitionIdentity = includeDefinitionIdentity
            ? new DefinitionIdentity
            {
                FeatureId = featureId,
                CharacteristicKey = characteristicKey,
                TargetSubgeometryIds = [targetSubgeometryId],
                IsDirect = true
            }
            : null,
        ValueEvidence = new AnnotationValueEvidence
        {
            RawDisplayedText = text,
            ValueKind = valueKind,
            NumericComparisonMode = comparisonMode,
            ParsedNumericValue = displayed,
            Units = units,
            CadUnits = cadUnits,
            DisplayResolution = displayResolution,
            NxAutomaticValue = automatic,
            CadValue = cad,
            MeasurementProvenance = "Direct NX regression evidence",
            ManualOverrideState = manualOverrideState,
            AssociationStatus = evidenceAssociation,
            ExtractionState = extractionState,
            Confidence = confidence,
            Authority = authority,
            AssociatedGeometry = [geometry],
            DefinedFeature = definedFeature
        }
    };
}

static DrawingAnnotation DashEvidence(
    string id,
    string featureId,
    string rawDisplayed,
    string parsedDesignation,
    string automaticDesignation,
    string? cadDesignation,
    EvidenceAuthority authority = EvidenceAuthority.DirectNx,
    ExtractionState extractionState = ExtractionState.Complete,
    string? portFamily = null,
    string? mappingRevision = null,
    IReadOnlyList<string>? candidateCadDesignations = null,
    ManualOverrideState manualOverrideState = ManualOverrideState.NotOverridden,
    AssociationStatus evidenceAssociation = AssociationStatus.Associated,
    bool isReference = false)
{
    var targetId = $"{featureId}:face";
    return new DrawingAnnotation(
        id,
        AnnotationKind.HoleCallout,
        rawDisplayed,
        null,
        null,
        0.000001,
        isReference,
        manualOverrideState == ManualOverrideState.Overridden,
        evidenceAssociation,
        [featureId],
        null,
        new EvidenceLocation("S1", "VIEW-RIGHT", 120, 80))
    {
        AnnotationObject = ObjectReference($"EVIDENCE:{id}", TagFrom(id, 1), "NXOpen.Annotations.Note"),
        DefinitionIdentity = new DefinitionIdentity
        {
            FeatureId = featureId,
            CharacteristicKey = "port.dashDesignation",
            TargetSubgeometryIds = [targetId],
            IsDirect = true
        },
        ValueEvidence = new AnnotationValueEvidence
        {
            RawDisplayedText = rawDisplayed,
            ValueKind = SemanticValueKind.DashDesignator,
            ParsedDesignation = parsedDesignation,
            NxAutomaticDesignation = automaticDesignation,
            CadDesignation = cadDesignation,
            MeasurementProvenance = authority == EvidenceAuthority.ApprovedLocalMapping
                ? "Approved local mapping regression evidence"
                : "Direct NX port feature parameter",
            ManualOverrideState = manualOverrideState,
            AssociationStatus = evidenceAssociation,
            ExtractionState = extractionState,
            Confidence = EvidenceConfidence.High,
            Authority = authority,
            AssociatedGeometry = [ObjectReference(targetId, TagFrom(targetId, 3), "NXOpen.Face", featureId)],
            DefinedFeature = ObjectReference(featureId, TagFrom(featureId, 2), "NXOpen.Features.Feature"),
            PortFamily = portFamily,
            MappingRevision = mappingRevision,
            CandidateCadDesignations = candidateCadDesignations
        }
    };
}

static NxObjectReference ObjectReference(
    string evidenceId,
    string nxTag,
    string objectType,
    params string[] directOwners) =>
    new()
    {
        EvidenceId = evidenceId,
        NxTag = nxTag,
        ObjectType = objectType,
        OwningPart = "TEST-PART-11",
        OwningRevision = "A",
        DirectOwnerEvidenceIds = directOwners
    };

static string TagFrom(string value, int suffix) =>
    $"{unchecked((uint)StringComparer.Ordinal.GetHashCode(value)):D10}{suffix}";

static AuditDocument LoadFixture(params string[] relativeSegments)
{
    var fixturePath = FindFixturePath(relativeSegments);
    return AuditDocumentLoader.Load(File.ReadAllText(fixturePath));
}

static string FindFixturePath(params string[] relativeSegments)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        var candidate = Path.Combine([directory.FullName, .. relativeSegments]);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    throw new FileNotFoundException($"Could not locate fixture '{Path.Combine(relativeSegments)}' from '{AppContext.BaseDirectory}'.");
}

static AuditFinding AssertSingle(AuditResult result, string ruleId)
{
    var findings = result.Findings.Where(finding => finding.RuleId == ruleId).ToArray();
    Equal(1, findings.Length, $"Expected exactly one {ruleId} finding.");
    return findings[0];
}

static AuditDiagnostic AssertDiagnostic(AuditResult result, string code, string annotationId)
{
    var diagnostics = (result.Diagnostics ?? Array.Empty<AuditDiagnostic>())
        .Where(diagnostic => diagnostic.Code == code && diagnostic.AnnotationId == annotationId)
        .ToArray();
    Equal(1, diagnostics.Length, $"Expected exactly one {code} diagnostic for {annotationId}.");
    return diagnostics[0];
}

static void AssertNoDiagnostic(AuditResult result, string code, string annotationId)
{
    var count = (result.Diagnostics ?? Array.Empty<AuditDiagnostic>())
        .Count(diagnostic => diagnostic.Code == code && diagnostic.AnnotationId == annotationId);
    Equal(0, count, $"Did not expect {code} for {annotationId}.");
}

static void AssertNoFinding(AuditResult result, string ruleId, string message) =>
    Equal(0, result.Findings.Count(finding => finding.RuleId == ruleId), message);

static void AssertValidationCode(Action action, string expectedCode)
{
    try
    {
        action();
    }
    catch (AuditDocumentValidationException exception)
    {
        True(
            exception.Issues.Any(issue => issue.Code == expectedCode),
            $"Expected validation code {expectedCode}; actual codes: {string.Join(", ", exception.Issues.Select(issue => issue.Code))}.");
        return;
    }

    throw new InvalidOperationException($"Expected AuditDocumentValidationException containing {expectedCode}.");
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    catch (Exception exception)
    {
        throw new InvalidOperationException(
            $"{message} Expected {typeof(TException).Name}; actual {exception.GetType().Name}.",
            exception);
    }

    throw new InvalidOperationException($"{message} Expected {typeof(TException).Name}.");
}

static void Contains(string actual, string expected, string message)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message} Missing '{expected}'.");
    }
}

static void DoesNotContain(string actual, string unexpected, string message)
{
    if (actual.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{message} Unexpected '{unexpected}'.");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message} Expected {expected}; actual {actual}.");
    }
}
