[CmdletBinding()]
param(
    [Parameter()]
    [string]$SetupPath,

    [Parameter()]
    [switch]$CleanupStale
)

$ErrorActionPreference = 'Stop'
$systemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
if ($CleanupStale)
{
    foreach ($staleRoot in Get-ChildItem -LiteralPath $systemTemp -Directory -Filter 'ddrgp-velopack-install-*')
    {
        $resolvedStaleRoot = [System.IO.Path]::GetFullPath($staleRoot.FullName)
        if (-not $resolvedStaleRoot.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase))
        {
            throw 'Refusing stale cleanup outside the system temp directory.'
        }
        Get-Process -Name DDRGpScoreViewer -ErrorAction SilentlyContinue |
            Where-Object {
                try { $_.MainModule.FileName.StartsWith($resolvedStaleRoot, [StringComparison]::OrdinalIgnoreCase) }
                catch { $false }
            } |
            Stop-Process -Force
        $staleUpdater = Join-Path $resolvedStaleRoot 'install\Update.exe'
        if (Test-Path -LiteralPath $staleUpdater -PathType Leaf)
        {
            & $staleUpdater uninstall --silent --rootDir (Join-Path $resolvedStaleRoot 'install') | Out-Null
            Start-Sleep -Seconds 2
        }
        Remove-Item -LiteralPath $resolvedStaleRoot -Recurse -Force
    }
    if ([string]::IsNullOrWhiteSpace($SetupPath))
    {
        Write-Output 'Stale VeloPack smoke directories were cleaned up.'
        return
    }
}
if ([string]::IsNullOrWhiteSpace($SetupPath))
{
    throw 'SetupPath is required unless CleanupStale is used.'
}
$resolvedSetup = [System.IO.Path]::GetFullPath($SetupPath)
if (-not (Test-Path -LiteralPath $resolvedSetup -PathType Leaf))
{
    throw "Setup was not found: $resolvedSetup"
}

$productionEvidence = [System.Collections.Generic.List[string]]::new()
$localApplicationData = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)
$productionInstallRoot = Join-Path `
    $localApplicationData `
    'com.tts1374.ddrgp_scorelog'
if (Test-Path -LiteralPath $productionInstallRoot)
{
    $productionEvidence.Add("install root: $productionInstallRoot")
}
$productionStartMenu = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::StartMenu)
$productionShortcut = Get-ChildItem `
    -LiteralPath $productionStartMenu `
    -Recurse `
    -Filter 'GP Score Log.lnk' `
    -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -ne $productionShortcut)
{
    $productionEvidence.Add("Start Menu shortcut: $($productionShortcut.FullName)")
}
$uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
if (Test-Path -LiteralPath $uninstallRoot)
{
    $productionRegistration = Get-ChildItem -LiteralPath $uninstallRoot |
        Where-Object {
            $properties = Get-ItemProperty -LiteralPath $_.PSPath
            $_.PSChildName -eq 'com.tts1374.ddrgp_scorelog' -or
                $properties.DisplayName -eq 'GP Score Log' -or
                $properties.InstallLocation -like `
                    '*\com.tts1374.ddrgp_scorelog*'
        } |
        Select-Object -First 1
    if ($null -ne $productionRegistration)
    {
        $productionEvidence.Add(
            "uninstall registration: $($productionRegistration.PSChildName)")
    }
}
if ($productionEvidence.Count -gt 0)
{
    throw @"
Refusing VeloPack installer smoke because a production GP Score Log installation was detected.
The installer uses the same per-user package identity, shortcut, and uninstall registration even
when --installto points at a temporary directory. Run this smoke only in a clean disposable
Windows environment. Detected: $($productionEvidence -join '; ')
"@
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    "ddrgp-velopack-install-$([guid]::NewGuid().ToString('N'))"
$installRoot = Join-Path $testRoot 'install'
$smokeDataRoot = Join-Path $testRoot 'local-data'
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$startMenu = [Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)
$installedProcess = $null
$startShortcut = $null

try
{
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $env:DDRGP_SCORE_VIEWER_RELEASE_SMOKE_ROOT = $smokeDataRoot
    $setupProcess = Start-Process `
        -FilePath $resolvedSetup `
        -ArgumentList @('--installto', $installRoot) `
        -PassThru
    $setupProcess.WaitForExit()
    if ($setupProcess.ExitCode -ne 0)
    {
        throw "Setup failed with exit code $($setupProcess.ExitCode)."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do
    {
        Start-Sleep -Milliseconds 250
        $installedProcess = Get-Process -Name DDRGpScoreViewer -ErrorAction SilentlyContinue |
            Where-Object {
                try
                {
                    $_.MainModule.FileName.StartsWith(
                        $installRoot,
                        [StringComparison]::OrdinalIgnoreCase)
                }
                catch
                {
                    $false
                }
            } |
            Select-Object -First 1
    }
    while ($null -eq $installedProcess -and [DateTime]::UtcNow -lt $deadline)
    if ($null -eq $installedProcess)
    {
        throw 'Installed application did not launch after setup.'
    }

    $startShortcut = Get-ChildItem `
        -LiteralPath $startMenu `
        -Recurse `
        -Filter 'GP Score Log.lnk' `
        -ErrorAction SilentlyContinue |
        Select-Object -First 1
    $desktopShortcut = Get-ChildItem `
        -LiteralPath $desktop `
        -Filter 'GP Score Log.lnk' `
        -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $startShortcut)
    {
        throw 'Start Menu shortcut was not created.'
    }
    if ($null -ne $desktopShortcut)
    {
        throw "Unexpected Desktop shortcut: $($desktopShortcut.FullName)"
    }

    $productionRoot = Join-Path $smokeDataRoot 'DDRGpScoreViewer'
    $requiredPaths = @(
            'data\master\ddrgp-master.sqlite'
            'data\master\jacket-catalog.sqlite'
            'data\master\reference-set.json'
            'data\score\score.db'
            'viewer-paths.json'
            'logs\gp-score-log.log') |
        ForEach-Object { Join-Path $productionRoot $_ }
    $dataDeadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $dataDeadline -and
        ($requiredPaths | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -gt 0)
    {
        Start-Sleep -Milliseconds 250
    }
    foreach ($requiredPath in $requiredPaths)
    {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf))
        {
            throw "Installed app did not prepare persistent data: $requiredPath"
        }
    }

    $scorePath = Join-Path $productionRoot 'data\score\score.db'
    $settingsPath = Join-Path $productionRoot 'viewer-paths.json'
    $backupRoot = Join-Path $testRoot 'manual-backup'
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    Copy-Item -LiteralPath $scorePath -Destination (Join-Path $backupRoot 'score.db')
    Copy-Item -LiteralPath $settingsPath -Destination (Join-Path $backupRoot 'viewer-paths.json')
    $backupScoreHash = (Get-FileHash -LiteralPath (Join-Path $backupRoot 'score.db') -Algorithm SHA256).Hash
    $backupSettingsHash = (Get-FileHash -LiteralPath (Join-Path $backupRoot 'viewer-paths.json') -Algorithm SHA256).Hash
    $installedProcess.Kill($true)
    $installedProcess.WaitForExit()
    $installedProcess = $null
    [System.IO.File]::WriteAllText(
        $scorePath,
        'existing-non-empty-score-db-must-not-be-overwritten',
        [System.Text.UTF8Encoding]::new($false))
    $scoreHashBeforeUpdate = (Get-FileHash -LiteralPath $scorePath -Algorithm SHA256).Hash
    $settingsHashBeforeUpdate = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash

    $updateProcess = Start-Process `
        -FilePath $resolvedSetup `
        -ArgumentList @('--silent', '--installto', $installRoot) `
        -PassThru
    $updateProcess.WaitForExit()
    if ($updateProcess.ExitCode -ne 0)
    {
        throw "Overwrite setup failed with exit code $($updateProcess.ExitCode)."
    }
    if ((Get-FileHash -LiteralPath $scorePath -Algorithm SHA256).Hash -ne $scoreHashBeforeUpdate)
    {
        throw 'Overwrite setup changed the existing non-empty score database.'
    }
    if ((Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash -ne $settingsHashBeforeUpdate)
    {
        throw 'Overwrite setup changed the existing settings file.'
    }

    Move-Item -LiteralPath $scorePath -Destination (Join-Path $productionRoot 'data\score\score.before-restore.db')
    Move-Item -LiteralPath $settingsPath -Destination (Join-Path $productionRoot 'viewer-paths.before-restore.json')
    Copy-Item -LiteralPath (Join-Path $backupRoot 'score.db') -Destination $scorePath
    Copy-Item -LiteralPath (Join-Path $backupRoot 'viewer-paths.json') -Destination $settingsPath
    if ((Get-FileHash -LiteralPath $scorePath -Algorithm SHA256).Hash -ne $backupScoreHash -or
        (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash -ne $backupSettingsHash)
    {
        throw 'Manual backup restore did not reproduce score DB and settings.'
    }

    $restoredApp = Join-Path $installRoot 'current\DDRGpScoreViewer.exe'
    $installedProcess = Start-Process -FilePath $restoredApp -PassThru
    if (-not $installedProcess.WaitForInputIdle(10000))
    {
        throw 'Restored application did not reach the input-idle boundary.'
    }

    Write-Output "Installed application launched: $installRoot"
    Write-Output "Start Menu shortcut created: $($startShortcut.FullName)"
    Write-Output 'Desktop shortcut was not created.'
    Write-Output "Persistent data prepared outside install root: $productionRoot"
    Write-Output 'Overwrite setup preserved the existing non-empty score DB and settings.'
    Write-Output 'Manual backup and restore reproduced the formal score DB and settings and reopened the app.'
}
finally
{
    if ($null -ne $installedProcess -and -not $installedProcess.HasExited)
    {
        $installedProcess.Kill($true)
        $installedProcess.WaitForExit()
    }

    $updater = Join-Path $installRoot 'Update.exe'
    if (Test-Path -LiteralPath $updater -PathType Leaf)
    {
        & $updater uninstall --silent --rootDir $installRoot | Out-Null
        Start-Sleep -Seconds 2
    }
    if ($null -ne $startShortcut -and (Test-Path -LiteralPath $startShortcut.FullName))
    {
        throw "Uninstall did not remove Start Menu shortcut: $($startShortcut.FullName)"
    }
    if (Test-Path -LiteralPath (Join-Path $smokeDataRoot 'DDRGpScoreViewer\data\score\score.db') -PathType Leaf)
    {
        Write-Output 'Uninstall preserved persistent score data outside the install root.'
    }

    if (Test-Path -LiteralPath $testRoot)
    {
        $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (-not $resolvedTestRoot.StartsWith(
                $tempRoot,
                [StringComparison]::OrdinalIgnoreCase))
        {
            throw 'Refusing cleanup outside the system temp directory.'
        }
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
    Remove-Item Env:DDRGP_SCORE_VIEWER_RELEASE_SMOKE_ROOT -ErrorAction SilentlyContinue
}
