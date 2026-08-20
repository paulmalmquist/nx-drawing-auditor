[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Relativity.DrawingAudit\nx-environment.json'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "NXOPEN_BUILD_GATE_001: Manifest not found at '$ManifestPath'. Run Find-NxOpen.ps1 first."
}

$resolvedManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne '1.0') {
    throw "NXOPEN_BUILD_GATE_002: Unsupported environment manifest schema '$($manifest.schemaVersion)'."
}

$blockers = @($manifest.blockers)
if (-not $manifest.gateReady -or $blockers.Count -gt 0) {
    $summary = @($blockers | ForEach-Object { "$($_.code): $($_.message)" }) -join [Environment]::NewLine
    throw "NXOPEN_BUILD_GATE_003: The workstation gate is not ready.$([Environment]::NewLine)$summary"
}

if ($manifest.session.mode -ne 'native') {
    throw "NXOPEN_BUILD_GATE_004: Only a native NX session is permitted; manifest mode is '$($manifest.session.mode)'."
}
if ($manifest.template.targetFramework -ne 'net8.0') {
    throw "NXOPEN_BUILD_GATE_005: Installed template target '$($manifest.template.targetFramework)' is not net8.0. Stop and re-plan."
}
if ($manifest.template.platformTarget -ne 'x64') {
    throw "NXOPEN_BUILD_GATE_006: Installed template platform '$($manifest.template.platformTarget)' is not x64."
}
if (-not $manifest.recordedJournal.signatureVerifiedAgainstTemplate) {
    throw 'NXOPEN_BUILD_GATE_007: Journal entry-point signature has not been verified against the installed template.'
}

$nxOpenPath = [string]$manifest.selectedInstallation.nxOpen.path
$nxOpenUfPath = [string]$manifest.selectedInstallation.nxOpenUf.path
if (-not (Test-Path -LiteralPath $nxOpenPath -PathType Leaf) -or -not (Test-Path -LiteralPath $nxOpenUfPath -PathType Leaf)) {
    throw 'NXOPEN_BUILD_GATE_008: One or both exact Siemens assembly paths no longer exist.'
}

$nxOpenIdentity = [Reflection.AssemblyName]::GetAssemblyName($nxOpenPath)
$nxOpenUfIdentity = [Reflection.AssemblyName]::GetAssemblyName($nxOpenUfPath)
if ($nxOpenIdentity.Name -ne 'NXOpen' -or $nxOpenUfIdentity.Name -ne 'NXOpen.UF') {
    throw 'NXOPEN_BUILD_GATE_009: Exact assembly identities are not NXOpen and NXOpen.UF.'
}
if ($nxOpenIdentity.Version -ne $nxOpenUfIdentity.Version) {
    throw "NXOPEN_BUILD_GATE_010: Siemens assembly versions differ ($($nxOpenIdentity.Version) vs $($nxOpenUfIdentity.Version))."
}
if (-not [string]::Equals(
        [IO.Path]::GetDirectoryName($nxOpenPath),
        [IO.Path]::GetDirectoryName($nxOpenUfPath),
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'NXOPEN_BUILD_GATE_011: Siemens assemblies are not from the same managed-assembly directory.'
}

$projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Relativity.DrawingAudit.NxOpen\Relativity.DrawingAudit.NxOpen.csproj'
$arguments = @(
    'build',
    $projectPath,
    '--configuration', $Configuration,
    '--framework', 'net8.0',
    '-p:EnableNxOpen=true',
    '-p:NxWorkstationGatePassed=true',
    "-p:NxEnvironmentManifestPath=$resolvedManifestPath",
    "-p:NXOpenDllPath=$nxOpenPath",
    "-p:NXOpenUfDllPath=$nxOpenUfPath",
    "-p:NxTemplateTargetFramework=$($manifest.template.targetFramework)",
    "-p:NxTemplatePlatformTarget=$($manifest.template.platformTarget)"
)

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "NXOPEN_BUILD_FAILED: dotnet build exited with code $LASTEXITCODE."
}
