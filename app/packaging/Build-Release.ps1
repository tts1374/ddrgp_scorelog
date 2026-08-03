[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter()]
    [string]$MasterDatabase = (Join-Path $PSScriptRoot '..\..\databases\ddrgp-master.sqlite'),

    [Parameter()]
    [string]$CatalogDatabase = (Join-Path $PSScriptRoot '..\..\databases\jacket-catalog.sqlite'),

    [Parameter()]
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\..\data\releases\$Version"),

    [Parameter()]
    [switch]$ValidateInputsOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$masterPath = [System.IO.Path]::GetFullPath($MasterDatabase)
$catalogPath = [System.IO.Path]::GetFullPath($CatalogDatabase)
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'data\releases'))
$buildRoot = Join-Path $repositoryRoot "data\release-build\$Version"
$publishDirectory = Join-Path $buildRoot 'publish'
$referenceDirectory = Join-Path $publishDirectory 'ReferenceData'

if (-not (Test-Path -LiteralPath $masterPath -PathType Leaf))
{
    throw "Master database was not found: $masterPath"
}
if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf))
{
    throw "Jacket catalog database was not found: $catalogPath"
}
if (-not $outputPath.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))
{
    throw "OutputDirectory must remain under $releaseRoot"
}

$pairJson = uv run --directory $repositoryRoot python -m tools.vision_poc.jacket_reference_catalog release-pair `
    --master-db $masterPath `
    --catalog $catalogPath
if ($LASTEXITCODE -ne 0)
{
    throw 'Release reference DB pair validation failed.'
}
$pair = $pairJson | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$pair.master_version) -or
    [string]::IsNullOrWhiteSpace([string]$pair.catalog_master_version) -or
    $pair.master_version -ne $pair.catalog_master_version)
{
    throw 'Release reference DB metadata versions do not match.'
}
if ($ValidateInputsOnly)
{
    Write-Output "Release reference DB pair validation passed: master_version=$($pair.master_version)"
    return
}

if (Test-Path -LiteralPath $buildRoot)
{
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}
if (Test-Path -LiteralPath $outputPath)
{
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $referenceDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

dotnet restore `
    (Join-Path $repositoryRoot 'app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj') `
    --locked-mode `
    --runtime win-x64
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }

dotnet publish `
    (Join-Path $repositoryRoot 'app\src\DDRGpScoreViewer\DDRGpScoreViewer.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $publishDirectory `
    -p:Version=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }

Copy-Item -LiteralPath $masterPath -Destination (Join-Path $referenceDirectory 'ddrgp-master.sqlite')
Copy-Item -LiteralPath $catalogPath -Destination (Join-Path $referenceDirectory 'jacket-catalog.sqlite')
$manifest = [ordered]@{
    content_version = $Version
    master_schema_version = 1
    catalog_schema_version = 1
    master_content_version = [string]$pair.master_version
    catalog_master_content_version = [string]$pair.catalog_master_version
    master_sha256 = (Get-FileHash -LiteralPath $masterPath -Algorithm SHA256).Hash.ToLowerInvariant()
    catalog_sha256 = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifestJson = ($manifest | ConvertTo-Json) + "`n"
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText(
    (Join-Path $referenceDirectory 'reference-set.json'),
    $manifestJson,
    $utf8NoBom)

& (Join-Path $repositoryRoot 'app\tests\VerifyReleaseBuild.ps1') `
    -AssemblyPath (Join-Path $publishDirectory 'DDRGpScoreViewer.dll')
& (Join-Path $repositoryRoot 'app\tests\VerifyReleaseRuntime.ps1') `
    -PublishDirectory $publishDirectory

dotnet vpk pack `
    --packId com.tts1374.ddrgp_scorelog `
    --packVersion $Version `
    --packDir $publishDirectory `
    --mainExe DDRGpScoreViewer.exe `
    --packTitle 'GP Score Log' `
    --shortcuts StartMenuRoot `
    --runtime win-x64 `
    --outputDir $outputPath
if ($LASTEXITCODE -ne 0) { throw 'VeloPack package creation failed.' }

Write-Output "VeloPack release created: $outputPath"
