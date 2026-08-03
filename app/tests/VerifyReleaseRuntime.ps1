[CmdletBinding()]
param(
    [Parameter()]
    [string]$PublishDirectory = (Join-Path `
        $PSScriptRoot `
        '..\src\DDRGpScoreViewer\bin\Release\net10.0-windows10.0.19041.0')
)

$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
$executablePath = Join-Path $resolvedPublishDirectory 'DDRGpScoreViewer.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf))
{
    throw "Release executable was not found: $executablePath"
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    "ddrgp-score-viewer-release-smoke-$([guid]::NewGuid().ToString('N'))"
$packageRoot = Join-Path $tempRoot 'package'
$localApplicationData = Join-Path $tempRoot 'local-app-data'
$resolvedRepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$process = $null

try
{
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $localApplicationData -Force | Out-Null
    Get-ChildItem -LiteralPath $resolvedPublishDirectory -Force |
        Copy-Item -Destination $packageRoot -Recurse -Force

    if ((Get-ChildItem -LiteralPath $packageRoot -Recurse -Force |
            Where-Object { $_.FullName -like '*tools\vision_poc*' -or $_.FullName -like '*tools/vision_poc*' }).Count -gt 0)
    {
        throw 'The temporary Release package contains tools\vision_poc.'
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = Join-Path $packageRoot 'DDRGpScoreViewer.exe'
    $startInfo.WorkingDirectory = $packageRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Environment['DDRGP_SCORE_VIEWER_RELEASE_SMOKE_ROOT'] = $localApplicationData
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start())
    {
        throw 'Release package process could not be started.'
    }

    if (-not $process.WaitForInputIdle(10000))
    {
        throw 'Release package did not reach the WPF input-idle boundary.'
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do
    {
        Start-Sleep -Milliseconds 200
        $children = Get-CimInstance Win32_Process |
            Where-Object { $_.ParentProcessId -eq $process.Id }
        foreach ($child in $children)
        {
            $commandLine = [string]$child.CommandLine
            if ($commandLine -match '(?i)python|tesseract|tools[\\/]vision_poc' -or
                $commandLine.IndexOf($resolvedRepositoryRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
            {
                throw "Release package started a forbidden child process: $commandLine"
            }
        }
    }
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)

    if ($process.HasExited -and $process.ExitCode -ne 0)
    {
        throw "Release package exited during smoke test with code $($process.ExitCode)."
    }

    $productionRoot = Join-Path $localApplicationData 'DDRGpScoreViewer'
    $requiredPaths = @((Join-Path $productionRoot 'logs\gp-score-log.log'))
    if (Test-Path -LiteralPath (Join-Path $packageRoot 'ReferenceData') -PathType Container)
    {
        $requiredPaths += @(
            (Join-Path $productionRoot 'data\master\ddrgp-master.sqlite')
            (Join-Path $productionRoot 'data\master\jacket-catalog.sqlite')
            (Join-Path $productionRoot 'data\master\reference-set.json')
            (Join-Path $productionRoot 'data\score\score.db'))
    }

    foreach ($requiredPath in $requiredPaths)
    {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf))
        {
            throw "Release package did not prepare required production data: $requiredPath"
        }
    }

    Write-Output "Release runtime smoke passed outside the repository: $packageRoot"
}
finally
{
    if ($null -ne $process)
    {
        try
        {
            if (-not $process.HasExited)
            {
                $process.Kill($true)
                $process.WaitForExit()
            }
        }
        catch [System.InvalidOperationException]
        {
            # The process may have exited during the final cleanup check.
        }
        $process.Dispose()
    }
    if (Test-Path -LiteralPath $tempRoot)
    {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
