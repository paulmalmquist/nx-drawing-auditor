param(
    [string]$Destination = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'third_party')
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Path $Destination -Force | Out-Null

$repositories = @(
    @{ Name = 'occt'; Url = 'https://github.com/Open-Cascade-SAS/OCCT.git' },
    @{ Name = 'ezdxf'; Url = 'https://github.com/mozman/ezdxf.git' },
    @{ Name = 'nist-sfa'; Url = 'https://github.com/usnistgov/SFA.git' },
    @{ Name = 'autocad-mcp'; Url = 'https://github.com/U-C4N/Autocad-MCP.git' },
    @{ Name = 'cadrip'; Url = 'https://github.com/zackska/cadRip.git' },
    @{ Name = 'engvision'; Url = 'https://github.com/seiggy/EngVision.git' }
)

foreach ($repository in $repositories) {
    $target = Join-Path $Destination $repository.Name
    if (Test-Path -LiteralPath $target) {
        Write-Output "Already present: $target"
        continue
    }

    git clone --depth 1 $repository.Url $target
    if ($LASTEXITCODE -ne 0) {
        throw "Clone failed: $($repository.Url)"
    }
}

$edocrTarget = Join-Path $Destination 'edocr2.git'
if (-not (Test-Path -LiteralPath $edocrTarget)) {
    git clone --depth 1 --bare 'https://github.com/javvi51/edocr2.git' $edocrTarget
    if ($LASTEXITCODE -ne 0) {
        throw 'Bare clone failed: eDOCr2'
    }
}

$nxTarget = Join-Path $Destination 'nxopen-lib'
if (-not (Test-Path -LiteralPath $nxTarget)) {
    git clone --depth 1 --filter=blob:none --sparse 'https://github.com/ugopen/nxopen_lib.git' $nxTarget
    if ($LASTEXITCODE -ne 0) {
        throw 'Sparse clone failed: nxopen_lib'
    }

    Push-Location $nxTarget
    try {
        git sparse-checkout set --no-cone '/LICENSE' '/NX2406/UGOPEN/uf_drf*.h' '/NX2406/UGOPEN/NXOpen/Annotations_*.hxx' '/NX2406/UGOPEN/NXOpen/Drawings_*.hxx' '/NX12.0.2.9/UGOPEN/SampleNXOpenApplications/C++/PMIExample/*'
        if ($LASTEXITCODE -ne 0) {
            throw 'Sparse checkout failed: nxopen_lib'
        }
    }
    finally {
        Pop-Location
    }
}

Write-Output "Research repositories are available in $Destination"
