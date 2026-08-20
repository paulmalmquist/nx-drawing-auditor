using System.Text.Json;
using Relativity.DrawingAudit.Core;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: drawing-audit <audit-input.json> [output-directory]");
    return 2;
}

try
{
    var inputPath = Path.GetFullPath(args[0]);
    var outputDirectory = Path.GetFullPath(args.Length == 2 ? args[1] : "artifacts");

    await using var input = File.OpenRead(inputPath);
    var document = await AuditDocumentLoader.LoadAsync(input);
    var result = AuditEngine.CreateDefault().Run(document);

    Directory.CreateDirectory(outputDirectory);
    var jsonPath = Path.Combine(outputDirectory, "audit-result.json");
    var htmlPath = Path.Combine(outputDirectory, "audit-report.html");
    await WriteAtomicallyAsync(jsonPath, AuditJson.Serialize(result));
    await WriteAtomicallyAsync(htmlPath, AuditHtmlRenderer.Render(result));

    Console.WriteLine($"Audited {document.Drawing.PartNumber} rev {document.Drawing.Revision}");
    Console.WriteLine($"Findings: {result.Findings.Count}");
    Console.WriteLine($"Diagnostics: {result.Diagnostics?.Count ?? 0}");
    Console.WriteLine(jsonPath);
    Console.WriteLine(htmlPath);
    return result.Findings.Any(finding => finding.Severity == FindingSeverity.Error) ? 1 : 0;
}
catch (AuditDocumentValidationException exception)
{
    Console.Error.WriteLine("Audit input validation failed.");
    foreach (var issue in exception.Issues)
    {
        Console.Error.WriteLine($"{issue.Path} [{issue.Code}] {issue.Message}");
    }

    return 2;
}
catch (JsonException exception)
{
    Console.Error.WriteLine($"Audit input is not valid JSON: {exception.Message}");
    return 2;
}
catch (IOException exception)
{
    Console.Error.WriteLine($"Audit processing failed: {exception.Message}");
    return 2;
}
catch (UnauthorizedAccessException exception)
{
    Console.Error.WriteLine($"Audit processing failed: {exception.Message}");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Audit processing failed safely: {exception.Message}");
    return 2;
}

static async Task WriteAtomicallyAsync(string destinationPath, string contents)
{
    var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
    try
    {
        await File.WriteAllTextAsync(temporaryPath, contents);
        File.Move(temporaryPath, destinationPath, overwrite: true);
    }
    finally
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }
}
