namespace Relativity.DrawingAudit.Core;

public static class RuleCatalog
{
    private static readonly IReadOnlyDictionary<string, RuleMetadata> Rules =
        new Dictionary<string, RuleMetadata>(StringComparer.Ordinal)
        {
            ["DRAWING_CAD_VALUE_MISMATCH"] = Pending(
                "DRAWING_CAD_VALUE_MISMATCH",
                FindingSeverity.Error,
                "A displayed engineering value must agree with authoritative CAD evidence at the drawing's stated resolution.",
                "Direct feature identity, complete authoritative value evidence, display semantics, and high confidence",
                "Emit an operational diagnostic and no mismatch conclusion."),
            ["MANUAL_DIMENSION_OVERRIDE"] = Pending(
                "MANUAL_DIMENSION_OVERRIDE",
                FindingSeverity.Warning,
                "A direct manual text override requires engineering review even when its displayed value agrees with CAD.",
                "Direct NX override state",
                "Emit an operational diagnostic when override state is unavailable."),
            ["BROKEN_DIMENSION_ASSOCIATIVITY"] = Pending(
                "BROKEN_DIMENSION_ASSOCIATIVITY",
                FindingSeverity.Error,
                "A defining dimension or hole callout must remain associated with authoritative model geometry.",
                "Direct NX association state",
                "Emit an operational diagnostic when association state could not be extracted."),
            ["DUPLICATE_FEATURE_DEFINITION"] = Pending(
                "DUPLICATE_FEATURE_DEFINITION",
                FindingSeverity.Error,
                "A feature characteristic should have one non-reference defining annotation unless an approved exception applies.",
                "Direct feature, characteristic, target-subgeometry, and reference-state evidence",
                "Emit an operational diagnostic and no duplicate conclusion."),
            ["FLATNESS_REFERENCES_DATUM"] = Pending(
                "FLATNESS_REFERENCES_DATUM",
                FindingSeverity.Error,
                "A flatness feature-control frame must not contain datum references.",
                "Parsed characteristic and datum-compartment evidence",
                "Emit an operational diagnostic when the frame cannot be parsed completely.")
        };

    public static IReadOnlyList<RuleMetadata> All { get; } =
        Rules.Values.OrderBy(rule => rule.RuleId, StringComparer.Ordinal).ToArray();

    public static RuleMetadata Get(string ruleId) =>
        Rules.TryGetValue(ruleId, out var metadata)
            ? metadata
            : throw new KeyNotFoundException($"No rule metadata is registered for '{ruleId}'.");

    public static bool TryGet(string ruleId, out RuleMetadata? metadata) =>
        Rules.TryGetValue(ruleId, out metadata);

    private static RuleMetadata Pending(
        string ruleId,
        FindingSeverity severity,
        string interpretation,
        string evidenceRequirements,
        string incompleteEvidenceBehavior) =>
        new()
        {
            RuleId = ruleId,
            StandardName = "Pending company approval",
            Edition = "Pending",
            ParagraphCitation = "Pending",
            Interpretation = interpretation,
            Applicability = "Drafting-review advisory; applicability profile pending engineering approval.",
            PermittedExceptions = "Company-approved exceptions only; catalog entry pending.",
            Severity = severity,
            EngineeringOwner = "Pending",
            ApprovalDate = null,
            ApprovalStatus = RuleApprovalStatus.Pending,
            EvidenceRequirements = [evidenceRequirements],
            IncompleteEvidenceBehavior = incompleteEvidenceBehavior
        };
}
