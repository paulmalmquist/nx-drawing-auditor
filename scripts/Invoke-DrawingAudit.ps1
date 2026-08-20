[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$RunDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$CliPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Test-Path Variable:\PSNativeCommandUseErrorActionPreference) {
    # Exit 1 means a successful audit that found errors, so it must not become a
    # PowerShell native-command exception.
    $PSNativeCommandUseErrorActionPreference = $false
}

trap {
    [Console]::Error.WriteLine("DRAWING_AUDIT_UNEXPECTED_FAILURE: $($_.Exception.Message)")
    exit 2
}

function Exit-ProcessingFailure {
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        [int]$ExitCode = 2
    )

    [Console]::Error.WriteLine($Message)
    exit ([Math]::Max(2, $ExitCode))
}

if (-not (Test-Path -LiteralPath $RunDirectory -PathType Container)) {
    Exit-ProcessingFailure "DRAWING_AUDIT_RUN_001: Run directory not found: $RunDirectory"
}

$resolvedRunDirectory = (Resolve-Path -LiteralPath $RunDirectory).Path
$inputPath = Join-Path $resolvedRunDirectory 'audit-input.json'
if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) {
    $diagnosticPath = Join-Path $resolvedRunDirectory 'extraction-diagnostic.json'
    if (Test-Path -LiteralPath $diagnosticPath -PathType Leaf) {
        Exit-ProcessingFailure "DRAWING_AUDIT_RUN_002: Extraction failed closed; audit-input.json was not produced. Review $diagnosticPath"
    }

    Exit-ProcessingFailure "DRAWING_AUDIT_RUN_003: The run is incomplete because audit-input.json is absent: $resolvedRunDirectory"
}

if ((Get-Item -LiteralPath $inputPath).Length -eq 0) {
    Exit-ProcessingFailure "DRAWING_AUDIT_RUN_004: audit-input.json is empty: $inputPath"
}

$resolvedCliPath = $null
if (Test-Path -LiteralPath $CliPath -PathType Container) {
    $candidateExecutable = Join-Path (Resolve-Path -LiteralPath $CliPath).Path 'Relativity.DrawingAudit.Cli.exe'
    if (Test-Path -LiteralPath $candidateExecutable -PathType Leaf) {
        $resolvedCliPath = (Resolve-Path -LiteralPath $candidateExecutable).Path
    }
}
elseif (Test-Path -LiteralPath $CliPath -PathType Leaf) {
    $candidateExecutable = (Resolve-Path -LiteralPath $CliPath).Path
    if ([IO.Path]::GetFileName($candidateExecutable) -eq 'Relativity.DrawingAudit.Cli.exe') {
        $resolvedCliPath = $candidateExecutable
    }
}

if ($null -eq $resolvedCliPath) {
    Exit-ProcessingFailure 'DRAWING_AUDIT_CLI_001: CliPath must be the intact self-contained publish directory or its Relativity.DrawingAudit.Cli.exe.'
}

$cliDirectory = [IO.Path]::GetDirectoryName($resolvedCliPath)
foreach ($requiredCompanion in @('Relativity.DrawingAudit.Cli.deps.json', 'Relativity.DrawingAudit.Cli.runtimeconfig.json')) {
    $companionPath = Join-Path $cliDirectory $requiredCompanion
    if (-not (Test-Path -LiteralPath $companionPath -PathType Leaf)) {
        Exit-ProcessingFailure "DRAWING_AUDIT_CLI_002: The self-contained publish is incomplete; missing $companionPath"
    }
}

$inputHashBefore = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
try {
    & $resolvedCliPath $inputPath $resolvedRunDirectory
    $auditExitCode = $LASTEXITCODE
}
catch {
    Exit-ProcessingFailure "DRAWING_AUDIT_CLI_003: The CLI could not be started: $($_.Exception.Message)"
}

if ($auditExitCode -notin @(0, 1)) {
    Exit-ProcessingFailure "DRAWING_AUDIT_CLI_004: Audit processing failed with CLI exit code $auditExitCode." -ExitCode $auditExitCode
}

$inputHashAfter = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
if ($inputHashBefore -ne $inputHashAfter) {
    Exit-ProcessingFailure 'DRAWING_AUDIT_SAFETY_001: audit-input.json changed while the CLI ran. Treat this run as invalid.'
}

foreach ($expectedOutput in @('audit-result.json', 'audit-report.html')) {
    $outputPath = Join-Path $resolvedRunDirectory $expectedOutput
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf) -or (Get-Item -LiteralPath $outputPath).Length -eq 0) {
        Exit-ProcessingFailure "DRAWING_AUDIT_OUTPUT_001: CLI exit $auditExitCode did not produce non-empty $outputPath"
    }
}

if ($auditExitCode -eq 0) {
    Write-Output "Audit completed with no error-severity findings: $resolvedRunDirectory"
}
else {
    Write-Output "Audit completed with error-severity findings: $resolvedRunDirectory"
}

exit $auditExitCode
