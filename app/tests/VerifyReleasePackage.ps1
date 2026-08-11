[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$packId = 'com.tts1374.ddrgp_scorelog'
$expectedTitle = 'GP Score Log'
$expectedAuthors = '2ten.'
$resolvedPackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$fullPackagePath = Join-Path $resolvedPackageDirectory "$packId-$Version-full.nupkg"
$setupPath = Join-Path $resolvedPackageDirectory "$packId-win-Setup.exe"

if (-not (Test-Path -LiteralPath $fullPackagePath -PathType Leaf))
{
    throw "Full VeloPack package was not found: $fullPackagePath"
}
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf))
{
    throw "VeloPack setup was not found: $setupPath"
}

Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($fullPackagePath)
try
{
    $nuspecEntry = $archive.Entries |
        Where-Object { $_.FullName -like '*.nuspec' } |
        Select-Object -First 1
    if ($null -eq $nuspecEntry)
    {
        throw 'VeloPack package does not contain a nuspec metadata file.'
    }

    $reader = New-Object System.IO.StreamReader(
        $nuspecEntry.Open(),
        [System.Text.UTF8Encoding]::new($false))
    try
    {
        [xml]$nuspec = $reader.ReadToEnd()
    }
    finally
    {
        $reader.Dispose()
    }

    $metadata = $nuspec.package.metadata
    if ($metadata.id -ne $packId)
    {
        throw "Package id mismatch: $($metadata.id)"
    }
    if ($metadata.version -ne $Version)
    {
        throw "Package version mismatch: $($metadata.version)"
    }
    if ($metadata.title -ne $expectedTitle -or
        $metadata.mainExe -ne 'DDRGpScoreViewer.exe')
    {
        throw "Package identity mismatch: title=$($metadata.title); mainExe=$($metadata.mainExe)"
    }
    if ($metadata.authors -ne $expectedAuthors)
    {
        throw "Package authors mismatch: $($metadata.authors)"
    }

    $iconEntry = $archive.Entries |
        Where-Object { $_.FullName -eq 'setup.ico' } |
        Select-Object -First 1
    if ($null -eq $iconEntry -or $iconEntry.Length -le 0)
    {
        throw 'VeloPack package does not contain a non-empty setup.ico.'
    }
}
finally
{
    $archive.Dispose()
}

Write-Output "Release package metadata verification passed: $fullPackagePath"
