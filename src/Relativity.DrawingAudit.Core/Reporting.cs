using System.Net;
using System.Text;

namespace Relativity.DrawingAudit.Core;

public static class AuditHtmlRenderer
{
    public static string Render(AuditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var findingRows = new StringBuilder();
        foreach (var finding in result.Findings)
        {
            var context = FindAnnotation(result.Document, finding.AnnotationId);
            findingRows.Append("<tr>")
                .Append(Cell(finding.Severity))
                .Append(Cell(finding.RuleId))
                .Append(Cell("Drafting-review advisory"))
                .Append(Cell(context?.Sheet.Name))
                .Append(Cell(context?.View.Name))
                .Append(Cell(finding.AnnotationId))
                .Append(Cell(finding.EvidenceId ?? context?.Annotation.AnnotationObject?.EvidenceId))
                .Append(Cell(finding.FeatureId))
                .Append(Cell(finding.Observed))
                .Append(Cell(finding.Expected))
                .Append(Cell(finding.Difference))
                .Append(Cell(finding.Assessment))
                .Append(Cell(finding.Confidence))
                .Append(Cell(finding.Message))
                .Append("</tr>");
        }

        var evidenceRows = new StringBuilder();
        foreach (var (sheet, view, annotation) in EnumerateAnnotations(result.Document))
        {
            var facts = EffectiveAnnotationFactsResolver.Resolve(result.Document, annotation);
            var geometryTags = string.Join(", ", facts.AssociatedGeometry.Select(item => item.NxTag));
            var geometryEvidence = string.Join(", ", facts.AssociatedGeometry.Select(item => item.EvidenceId));
            var annotationOwner = FormatOwner(annotation.AnnotationObject);
            var featureOwner = FormatOwner(facts.DefinedFeature);
            var automatic = facts.ValueKind == SemanticValueKind.DashDesignator
                ? facts.NxAutomaticDesignation
                : facts.NxAutomaticValue?.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
            var cad = facts.ValueKind == SemanticValueKind.DashDesignator
                ? facts.CadDesignation
                : facts.CadValue?.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);

            evidenceRows.Append("<tr>")
                .Append(Cell(sheet.Name))
                .Append(Cell(view.Name))
                .Append(Cell(annotation.Id))
                .Append(Cell(annotation.AnnotationObject?.NxTag))
                .Append(Cell(annotationOwner))
                .Append(Cell(facts.RawDisplayedText))
                .Append(Cell(automatic))
                .Append(Cell(cad))
                .Append(Cell(facts.ValueKind))
                .Append(Cell(facts.ManualOverrideState))
                .Append(Cell(facts.AssociationStatus))
                .Append(Cell(facts.ExtractionState))
                .Append(Cell(facts.Confidence))
                .Append(Cell(facts.Authority))
                .Append(Cell(facts.DefinitionIdentity?.FeatureId ?? facts.DefinedFeature?.EvidenceId))
                .Append(Cell(facts.DefinedFeature?.NxTag))
                .Append(Cell(featureOwner))
                .Append(Cell(geometryTags))
                .Append(Cell(geometryEvidence))
                .Append(Cell(facts.PortFamily))
                .Append(Cell(facts.MappingRevision))
                .Append(Cell(string.Join(", ", facts.CandidateCadDesignations)))
                .Append(Cell(facts.MeasurementProvenance))
                .Append("</tr>");
        }

        var diagnosticRows = new StringBuilder();
        foreach (var diagnostic in result.Diagnostics ?? Array.Empty<AuditDiagnostic>())
        {
            diagnosticRows.Append("<tr>")
                .Append(Cell(diagnostic.Severity))
                .Append(Cell(diagnostic.Code))
                .Append(Cell(diagnostic.AnnotationId))
                .Append(Cell(diagnostic.EvidenceId))
                .Append(Cell(diagnostic.RuleId))
                .Append(Cell(diagnostic.Message))
                .Append("</tr>");
        }

        var extraction = result.Document.ExtractionMetadata;
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <title>Drawing audit — {{Encode(result.Document.Drawing.PartNumber)}}</title>
              <style>
                body { font: 14px system-ui, sans-serif; margin: 2rem; color: #172033; }
                h1, h2 { color: #102a43; }
                .notice { border-left: .3rem solid #c47f00; background: #fff7e6; padding: .8rem; }
                table { border-collapse: collapse; width: 100%; margin-bottom: 2rem; }
                th, td { border: 1px solid #ccd3df; padding: .5rem; text-align: left; vertical-align: top; }
                th { background: #edf1f7; }
                tr:nth-child(even) { background: #f8fafc; }
              </style>
            </head>
            <body>
              <h1>Engineering drawing audit</h1>
              <p class="notice">This report is an evidence-backed drafting-review advisory. It is not an autonomous ASME compliance certification.</p>
              <p>Part {{Encode(result.Document.Drawing.PartNumber)}} · Revision {{Encode(result.Document.Drawing.Revision)}} · Units {{Encode(result.Document.Drawing.Units)}} · Profile {{Encode(result.Document.Drawing.StandardProfile)}} · Contract {{Encode(result.Document.SchemaVersion)}}</p>
              <p>Source {{Encode(extraction?.SourceSystem)}} · Run {{Encode(extraction?.RunId)}} · Extracted {{Encode(extraction?.ExtractedAtUtc)}}</p>
              <p>NX modified-state evidence · Drawing before {{Encode(extraction?.DrawingModifiedBefore)}} / after {{Encode(extraction?.DrawingModifiedAfter)}} · Referenced model before {{Encode(extraction?.ReferencedModelModifiedBefore)}} / after {{Encode(extraction?.ReferencedModelModifiedAfter)}}</p>
              <h2>Findings ({{result.Findings.Count}})</h2>
              <table>
                <thead><tr><th>Severity</th><th>Rule</th><th>Status</th><th>Sheet</th><th>View</th><th>Annotation</th><th>Evidence ID</th><th>Feature</th><th>Observed</th><th>Expected</th><th>Difference</th><th>Assessment</th><th>Confidence</th><th>Finding</th></tr></thead>
                <tbody>{{findingRows}}</tbody>
              </table>
              <h2>Annotation evidence</h2>
              <table>
                <thead><tr><th>Sheet</th><th>View</th><th>Annotation</th><th>Annotation NX tag</th><th>Annotation owner</th><th>Displayed</th><th>NX automatic</th><th>CAD</th><th>Value kind</th><th>Override</th><th>Association</th><th>Extraction</th><th>Confidence</th><th>Authority</th><th>Feature ID</th><th>Feature NX tag</th><th>Feature owner</th><th>Geometry NX tags</th><th>Geometry evidence IDs</th><th>Port family</th><th>Mapping revision</th><th>Mapping candidates</th><th>Provenance</th></tr></thead>
                <tbody>{{evidenceRows}}</tbody>
              </table>
              <h2>Operational diagnostics ({{result.Diagnostics?.Count ?? 0}})</h2>
              <table>
                <thead><tr><th>Severity</th><th>Code</th><th>Annotation</th><th>Evidence ID</th><th>Rule</th><th>Diagnostic</th></tr></thead>
                <tbody>{{diagnosticRows}}</tbody>
              </table>
            </body>
            </html>
            """;
    }

    private static (DrawingSheet Sheet, DrawingView View, DrawingAnnotation Annotation)? FindAnnotation(
        AuditDocument document,
        string annotationId)
    {
        foreach (var item in EnumerateAnnotations(document))
        {
            if (string.Equals(item.Annotation.Id, annotationId, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private static IEnumerable<(DrawingSheet Sheet, DrawingView View, DrawingAnnotation Annotation)> EnumerateAnnotations(
        AuditDocument document) =>
        document.Sheets.SelectMany(sheet => sheet.Views
            .SelectMany(view => view.Annotations.Select(annotation => (sheet, view, annotation))));

    private static string Cell(object? value) => $"<td>{Encode(value)}</td>";

    private static string? FormatOwner(NxObjectReference? reference) =>
        reference is null
            ? null
            : string.IsNullOrWhiteSpace(reference.OwningRevision)
                ? reference.OwningPart
                : $"{reference.OwningPart} rev {reference.OwningRevision}";

    private static string Encode(object? value) =>
        WebUtility.HtmlEncode(value?.ToString() ?? string.Empty);
}
