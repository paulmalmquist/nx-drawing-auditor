[CmdletBinding()]
param(
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$PackageSource
)

$ErrorActionPreference = 'Stop'
if (Test-Path Variable:\PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64\$timestamp"
}

$publishDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $publishDirectory) -and
    (Get-ChildItem -LiteralPath $publishDirectory -Force | Select-Object -First 1)) {
    throw "Publish output must be a new or empty directory: $publishDirectory"
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

$projectPath = Join-Path $repositoryRoot 'src\Relativity.DrawingAudit.Cli\Relativity.DrawingAudit.Cli.csproj'
# The source applies only to this publish restore. The script never adds or
# enables a persistent NuGet source; SDK reference packs come from installed SDKs.
dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --source $PackageSource `
    --output $publishDirectory `
    -p:DisableTransitiveFrameworkReferenceDownloads=true `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Self-contained CLI publish failed with exit code $LASTEXITCODE."
}

$executablePath = Join-Path $publishDirectory 'Relativity.DrawingAudit.Cli.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published CLI executable was not created: $executablePath"
}

$fixturePath = Join-Path $repositoryRoot 'examples\four-defect-audit.json'
$smokeOutput = Join-Path $repositoryRoot "artifacts\publish-smoke\$timestamp"
New-Item -ItemType Directory -Path $smokeOutput -Force | Out-Null

& $executablePath $fixturePath $smokeOutput
$auditExitCode = $LASTEXITCODE
if ($auditExitCode -ne 1) {
    throw "Published CLI smoke test returned $auditExitCode; expected 1 because the fixture contains error findings."
}

foreach ($expectedReport in @('audit-result.json', 'audit-report.html')) {
    $reportPath = Join-Path $smokeOutput $expectedReport
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "Published CLI smoke test did not create $reportPath"
    }
}

Write-Output "Self-contained CLI: $publishDirectory"
Write-Output "Smoke-test reports: $smokeOutput"
