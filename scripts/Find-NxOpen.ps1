[CmdletBinding()]
param(
    [string[]]$NxRoot,
    [string]$NXOpenDllPath,
    [string]$NXOpenUfDllPath,
    [string]$TemplateProjectPath,
    [string]$JournalPath,
    [switch]$JournalSelectionsConfirmed,
    [string]$DefectDrawingPath,
    [switch]$ReferencedModelLoaded,
    [string]$AuditProfilePath,
    [switch]$AuditProfileContentsConfirmed,
    [string]$SelfContainedCliPath,
    [ValidateSet('Native', 'Teamcenter', 'Unknown')]
    [string]$SessionMode = 'Unknown',
    [ValidateSet('Allowed', 'Prohibited', 'Unknown')]
    [string]$JournalExecutionPolicy = 'Unknown',
    [ValidateSet('Available', 'Unavailable', 'Unknown')]
    [string]$NxLicenseStatus = 'Unknown',
    [string]$OutputPath = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Relativity.DrawingAudit\nx-environment.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$blockers = [System.Collections.Generic.List[object]]::new()

function Add-Blocker {
    param(
        [Parameter(Mandatory)]
        [string]$Code,
        [Parameter(Mandatory)]
        [string]$Message
    )

    $blockers.Add([ordered]@{ code = $Code; message = $Message })
}

function Resolve-ExistingPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-AssemblyMetadata {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        $resolved = (Resolve-Path -LiteralPath $Path).Path
        $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($resolved)
        $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolved)
        return [ordered]@{
            path = $resolved
            name = $assemblyName.Name
            assemblyVersion = $assemblyName.Version.ToString()
            fileVersion = $fileVersion.FileVersion
            productVersion = $fileVersion.ProductVersion
            length = (Get-Item -LiteralPath $resolved).Length
            lastWriteUtc = (Get-Item -LiteralPath $resolved).LastWriteTimeUtc.ToString('O')
        }
    }
    catch {
        Add-Blocker -Code 'NX_DISCOVERY_ASSEMBLY_METADATA_FAILED' -Message "Could not read assembly metadata from '$Path': $($_.Exception.Message)"
        return $null
    }
}

function Get-ProjectSetting {
    param(
        [xml]$ProjectXml,
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $nodes = @($ProjectXml.SelectNodes("//*[local-name()='$name']"))
        $values = @($nodes | ForEach-Object { $_.InnerText.Trim() } | Where-Object { $_ } | Select-Object -Unique)
        if ($values.Count -gt 0) {
            return ($values -join ';')
        }
    }

    return $null
}

function Get-EntryPointSignatures {
    param([string[]]$Paths)

    $signatures = [System.Collections.Generic.List[string]]::new()
    foreach ($path in @($Paths)) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            continue
        }

        $content = Get-Content -LiteralPath $path -Raw
        $matches = [regex]::Matches(
            $content,
            '(?m)(?:public\s+)?static\s+(?:void|int)\s+(?:Main|GetUnloadOption)\s*\([^)]*\)')
        foreach ($match in $matches) {
            $signatures.Add(([regex]::Replace($match.Value, '\s+', ' ')).Trim())
        }
    }

    return @($signatures | Select-Object -Unique)
}

function Invoke-DotNetInventory {
    param([string]$Argument)

    try {
        return @(& dotnet $Argument 2>$null)
    }
    catch {
        return @()
    }
}

$rawRoots = @($NxRoot) + @($env:UGII_BASE_DIR, $env:UGII_ROOT_DIR)
$candidateRoots = @(
    $rawRoots |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Container) } |
        ForEach-Object { (Resolve-Path -LiteralPath $_).Path } |
        Select-Object -Unique
)

$exactPathMode = -not [string]::IsNullOrWhiteSpace($NXOpenDllPath) -or -not [string]::IsNullOrWhiteSpace($NXOpenUfDllPath)
$pairs = @()
if ($exactPathMode) {
    if ([string]::IsNullOrWhiteSpace($NXOpenDllPath) -or [string]::IsNullOrWhiteSpace($NXOpenUfDllPath)) {
        Add-Blocker -Code 'NX_DISCOVERY_EXACT_PAIR_REQUIRED' -Message 'Supply both NXOpenDllPath and NXOpenUfDllPath when selecting exact assemblies.'
    }
    elseif ((Test-Path -LiteralPath $NXOpenDllPath -PathType Leaf) -and (Test-Path -LiteralPath $NXOpenUfDllPath -PathType Leaf)) {
        $pairs = @([pscustomobject]@{
            NXOpenDllPath = (Resolve-Path -LiteralPath $NXOpenDllPath).Path
            NXOpenUfDllPath = (Resolve-Path -LiteralPath $NXOpenUfDllPath).Path
        })
    }
    else {
        Add-Blocker -Code 'NX_DISCOVERY_EXACT_ASSEMBLY_MISSING' -Message 'One or both exact NX Open assembly paths do not exist.'
    }
}
else {
    if ($candidateRoots.Count -eq 0) {
        Add-Blocker -Code 'NX_DISCOVERY_ROOT_MISSING' -Message 'No supplied NX root, UGII_BASE_DIR, or UGII_ROOT_DIR points to an installed directory.'
    }
    else {
        $assemblyFilesByPath = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($root in $candidateRoots) {
            Get-ChildItem -LiteralPath $root -Recurse -File -Filter 'NXOpen*.dll' -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -in @('NXOpen.dll', 'NXOpen.UF.dll') } |
                ForEach-Object { $assemblyFilesByPath[$_.FullName] = $_ }
        }

        $nxOpenFiles = @($assemblyFilesByPath.Values | Where-Object { $_.Name -eq 'NXOpen.dll' })
        foreach ($nxOpenFile in $nxOpenFiles) {
            $ufPath = Join-Path $nxOpenFile.DirectoryName 'NXOpen.UF.dll'
            if ($assemblyFilesByPath.ContainsKey($ufPath)) {
                $pairs += [pscustomobject]@{
                    NXOpenDllPath = $nxOpenFile.FullName
                    NXOpenUfDllPath = $ufPath
                }
            }
        }

        $pairs = @($pairs | Sort-Object NXOpenDllPath -Unique)
    }
}

if ($pairs.Count -eq 0 -and -not ($blockers | Where-Object { $_.code -like 'NX_DISCOVERY_EXACT_*' })) {
    Add-Blocker -Code 'NX_DISCOVERY_ASSEMBLIES_NOT_FOUND' -Message 'No co-located NXOpen.dll and NXOpen.UF.dll pair was found.'
}
elseif ($pairs.Count -gt 1) {
    Add-Blocker -Code 'NX_DISCOVERY_MULTIPLE_INSTALLATIONS' -Message 'Multiple NX Open assembly pairs were found. Re-run with exact NXOpenDllPath and NXOpenUfDllPath values.'
}

$selectedInstallation = $null
if ($pairs.Count -eq 1) {
    $nxOpenMetadata = Get-AssemblyMetadata -Path $pairs[0].NXOpenDllPath
    $nxOpenUfMetadata = Get-AssemblyMetadata -Path $pairs[0].NXOpenUfDllPath
    if ($null -ne $nxOpenMetadata -and $null -ne $nxOpenUfMetadata) {
        $nxOpenDirectory = [IO.Path]::GetDirectoryName($nxOpenMetadata.path)
        $nxOpenUfDirectory = [IO.Path]::GetDirectoryName($nxOpenUfMetadata.path)
        if (-not [string]::Equals($nxOpenDirectory, $nxOpenUfDirectory, [StringComparison]::OrdinalIgnoreCase)) {
            Add-Blocker -Code 'NX_DISCOVERY_ASSEMBLY_ROOT_MISMATCH' -Message 'NXOpen.dll and NXOpen.UF.dll are not in the same installed managed-assembly directory.'
        }
        if ($nxOpenMetadata.name -ne 'NXOpen' -or $nxOpenUfMetadata.name -ne 'NXOpen.UF') {
            Add-Blocker -Code 'NX_DISCOVERY_ASSEMBLY_IDENTITY_MISMATCH' -Message 'The selected files do not have NXOpen and NXOpen.UF assembly identities.'
        }
        if ($nxOpenMetadata.assemblyVersion -ne $nxOpenUfMetadata.assemblyVersion) {
            Add-Blocker -Code 'NX_DISCOVERY_ASSEMBLY_VERSION_MISMATCH' -Message 'NXOpen.dll and NXOpen.UF.dll have different assembly versions.'
        }

        $installationRoot = @(
            $candidateRoots |
                Where-Object { $nxOpenMetadata.path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) } |
                Sort-Object Length |
                Select-Object -First 1
        )
        if ($installationRoot.Count -eq 0) {
            $installationRoot = @($nxOpenDirectory)
        }

        $selectedInstallation = [ordered]@{
            root = $installationRoot[0]
            managedAssemblyDirectory = $nxOpenDirectory
            release = $nxOpenMetadata.productVersion
            build = $nxOpenMetadata.fileVersion
            nxOpen = $nxOpenMetadata
            nxOpenUf = $nxOpenUfMetadata
            assemblyVersionsCompatible = $nxOpenMetadata.assemblyVersion -eq $nxOpenUfMetadata.assemblyVersion
        }
    }
}

$resolvedTemplatePath = Resolve-ExistingPath -Path $TemplateProjectPath
$templateTargetFramework = $null
$templatePlatformTarget = $null
$templateSourcePaths = @()
if ($null -eq $resolvedTemplatePath) {
    Add-Blocker -Code 'NX_PREFLIGHT_TEMPLATE_REQUIRED' -Message 'Provide the installed NX managed template project path.'
}
else {
    try {
        [xml]$templateXml = Get-Content -LiteralPath $resolvedTemplatePath -Raw
        $templateTargetFramework = Get-ProjectSetting -ProjectXml $templateXml -Names @('TargetFramework', 'TargetFrameworks', 'TargetFrameworkVersion')
        $templatePlatformTarget = Get-ProjectSetting -ProjectXml $templateXml -Names @('PlatformTarget', 'Platforms')
        $templateDirectory = [IO.Path]::GetDirectoryName($resolvedTemplatePath)
        $templateSourcePaths = @(
            Get-ChildItem -LiteralPath $templateDirectory -File -Filter '*.cs' -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty FullName
        )
    }
    catch {
        Add-Blocker -Code 'NX_PREFLIGHT_TEMPLATE_UNREADABLE' -Message "The installed NX template could not be inspected: $($_.Exception.Message)"
    }
}

if ([string]::IsNullOrWhiteSpace($templateTargetFramework)) {
    Add-Blocker -Code 'NX_PREFLIGHT_TEMPLATE_FRAMEWORK_UNKNOWN' -Message 'The installed NX template target framework could not be determined.'
}
elseif ($templateTargetFramework -ne 'net8.0') {
    Add-Blocker -Code 'NX_PREFLIGHT_TEMPLATE_FRAMEWORK_UNSUPPORTED' -Message "The installed NX template targets '$templateTargetFramework', not net8.0. Stop and re-plan the Core compatibility floor."
}

if ([string]::IsNullOrWhiteSpace($templatePlatformTarget)) {
    Add-Blocker -Code 'NX_PREFLIGHT_TEMPLATE_PLATFORM_UNKNOWN' -Message 'The installed NX template platform target could not be determined.'
}
elseif ($templatePlatformTarget -ne 'x64') {
    Add-Blocker -Code 'NX_PREFLIGHT_TEMPLATE_PLATFORM_UNSUPPORTED' -Message "The installed NX template targets '$templatePlatformTarget', not x64."
}

$resolvedJournalPath = Resolve-ExistingPath -Path $JournalPath
if ($null -eq $resolvedJournalPath) {
    Add-Blocker -Code 'NX_PREFLIGHT_JOURNAL_REQUIRED' -Message 'Provide the recorded NX journal path.'
}
if (-not $JournalSelectionsConfirmed) {
    Add-Blocker -Code 'NX_PREFLIGHT_JOURNAL_SELECTIONS_UNCONFIRMED' -Message 'Confirm that the journal contains the five required annotation selections.'
}

$templateSignatures = Get-EntryPointSignatures -Paths $templateSourcePaths
$journalSignatures = Get-EntryPointSignatures -Paths @($resolvedJournalPath)
$templateHasMain = @($templateSignatures | Where-Object { $_ -match '\sMain\s*\(' }).Count -gt 0
$templateHasUnload = @($templateSignatures | Where-Object { $_ -match '\sGetUnloadOption\s*\(' }).Count -gt 0
$journalHasMain = @($journalSignatures | Where-Object { $_ -match '\sMain\s*\(' }).Count -gt 0
$journalHasUnload = @($journalSignatures | Where-Object { $_ -match '\sGetUnloadOption\s*\(' }).Count -gt 0
$entryPointVerified = $templateHasMain -and $templateHasUnload -and $journalHasMain -and $journalHasUnload
if (-not $entryPointVerified) {
    Add-Blocker -Code 'NX_PREFLIGHT_ENTRY_POINT_UNVERIFIED' -Message 'Main/GetUnloadOption signatures must be present in both the installed template and recorded journal.'
}

$resolvedDrawingPath = Resolve-ExistingPath -Path $DefectDrawingPath
if ($null -eq $resolvedDrawingPath) {
    Add-Blocker -Code 'NX_PREFLIGHT_DRAWING_REQUIRED' -Message 'Provide the accessible native defect drawing path.'
}
if ($SessionMode -ne 'Native') {
    Add-Blocker -Code 'NX_PREFLIGHT_NATIVE_SESSION_REQUIRED' -Message "Session mode is '$SessionMode'. This milestone requires operator-confirmed native NX."
}
if (-not $ReferencedModelLoaded) {
    Add-Blocker -Code 'NX_PREFLIGHT_REFERENCED_MODEL_UNCONFIRMED' -Message 'Confirm that the drawing referenced model is already loaded.'
}
if ($JournalExecutionPolicy -ne 'Allowed') {
    Add-Blocker -Code 'NX_PREFLIGHT_JOURNAL_POLICY_BLOCKED' -Message "Journal execution policy is '$JournalExecutionPolicy'."
}
if ($NxLicenseStatus -ne 'Available') {
    Add-Blocker -Code 'NX_PREFLIGHT_LICENSE_UNAVAILABLE' -Message "NX license status is '$NxLicenseStatus'."
}

$resolvedAuditProfilePath = Resolve-ExistingPath -Path $AuditProfilePath
if ($null -eq $resolvedAuditProfilePath) {
    Add-Blocker -Code 'NX_PREFLIGHT_AUDIT_PROFILE_REQUIRED' -Message 'Provide the local audit profile path.'
}
if (-not $AuditProfileContentsConfirmed) {
    Add-Blocker -Code 'NX_PREFLIGHT_AUDIT_PROFILE_UNCONFIRMED' -Message 'Confirm that the profile contains approved identity keys, tolerance, and port-family mapping.'
}

$resolvedCliPath = Resolve-ExistingPath -Path $SelfContainedCliPath
$cliExecutablePath = $null
if ($null -ne $resolvedCliPath) {
    if (Test-Path -LiteralPath $resolvedCliPath -PathType Container) {
        $candidateCli = Join-Path $resolvedCliPath 'Relativity.DrawingAudit.Cli.exe'
        $cliExecutablePath = Resolve-ExistingPath -Path $candidateCli
    }
    elseif ([IO.Path]::GetFileName($resolvedCliPath) -eq 'Relativity.DrawingAudit.Cli.exe') {
        $cliExecutablePath = $resolvedCliPath
    }
}
if ($null -eq $cliExecutablePath) {
    Add-Blocker -Code 'NX_PREFLIGHT_SELF_CONTAINED_CLI_REQUIRED' -Message 'Provide the intact self-contained CLI directory or Relativity.DrawingAudit.Cli.exe path.'
}

$pythonStubLocations = @()
foreach ($root in $candidateRoots) {
    $pythonStubLocations += @(
        Get-ChildItem -LiteralPath $root -Recurse -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '(?i)python.*stub|stub.*python' } |
            Select-Object -ExpandProperty FullName
    )
}
$pythonStubLocations = @($pythonStubLocations | Select-Object -Unique)

$manifest = [ordered]@{
    schemaVersion = '1.0'
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    computerName = [Environment]::MachineName
    candidateRoots = $candidateRoots
    selectedInstallation = $selectedInstallation
    template = [ordered]@{
        projectPath = $resolvedTemplatePath
        targetFramework = $templateTargetFramework
        platformTarget = $templatePlatformTarget
        entryPointSignatures = $templateSignatures
    }
    recordedJournal = [ordered]@{
        path = $resolvedJournalPath
        requiredSelectionsConfirmedByOperator = $JournalSelectionsConfirmed.IsPresent
        entryPointSignatures = $journalSignatures
        signatureVerifiedAgainstTemplate = $entryPointVerified
    }
    pythonStubLocations = $pythonStubLocations
    session = [ordered]@{
        mode = $SessionMode.ToLowerInvariant()
        confirmationSource = if ($SessionMode -eq 'Unknown') { 'not-confirmed' } else { 'operator-input' }
        referencedModelLoaded = $ReferencedModelLoaded.IsPresent
    }
    journalExecutionPolicy = [ordered]@{
        status = $JournalExecutionPolicy.ToLowerInvariant()
        confirmationSource = if ($JournalExecutionPolicy -eq 'Unknown') { 'not-confirmed' } else { 'operator-input' }
    }
    nxLicense = [ordered]@{
        status = $NxLicenseStatus.ToLowerInvariant()
        confirmationSource = if ($NxLicenseStatus -eq 'Unknown') { 'not-confirmed' } else { 'operator-input' }
    }
    defectDrawing = [ordered]@{
        path = $resolvedDrawingPath
        nativeConfirmed = $SessionMode -eq 'Native'
    }
    auditProfile = [ordered]@{
        path = $resolvedAuditProfilePath
        requiredContentsConfirmedByOperator = $AuditProfileContentsConfirmed.IsPresent
    }
    selfContainedCli = [ordered]@{
        path = $resolvedCliPath
        executablePath = $cliExecutablePath
    }
    dotnet = [ordered]@{
        sdks = @(Invoke-DotNetInventory -Argument '--list-sdks')
        runtimes = @(Invoke-DotNetInventory -Argument '--list-runtimes')
    }
    blockers = @($blockers)
    gateReady = $blockers.Count -eq 0
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($fullOutputPath)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$temporaryOutputPath = "$fullOutputPath.$PID.tmp"
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporaryOutputPath -Encoding utf8
Move-Item -LiteralPath $temporaryOutputPath -Destination $fullOutputPath -Force

Write-Output ([pscustomobject]@{
    ManifestPath = $fullOutputPath
    GateReady = $manifest.gateReady
    BlockerCount = $blockers.Count
})
