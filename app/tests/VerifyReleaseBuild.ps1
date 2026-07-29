[CmdletBinding()]
param(
    [Parameter()]
    [string]$AssemblyPath = (Join-Path `
        $PSScriptRoot `
        '..\src\DDRGpScoreViewer\bin\Release\net10.0-windows10.0.19041.0\DDRGpScoreViewer.dll')
)

$resolvedAssemblyPath = [System.IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $resolvedAssemblyPath -PathType Leaf))
{
    throw "Release assembly was not found: $resolvedAssemblyPath"
}

$assembly = [System.Reflection.Assembly]::LoadFrom($resolvedAssemblyPath)
$publicMethodFlags = [System.Reflection.BindingFlags]::Public -bor
    [System.Reflection.BindingFlags]::Instance -bor
    [System.Reflection.BindingFlags]::Static
$nonPublicInstanceMethodFlags = [System.Reflection.BindingFlags]::NonPublic -bor
    [System.Reflection.BindingFlags]::Instance
$types = $assembly.GetTypes()

$publicMethodNames = @(
    $types |
        ForEach-Object {
            $_.GetMethods($publicMethodFlags) |
                Select-Object -ExpandProperty Name
        }
)

$developerOnlyEntrances = @(
    'CaptureOneFrameAsync'
    'StartContinuousCaptureAsync'
    'SaveAndReloadConfiguredAsync'
    'SaveAndReloadAsync'
)
$unexpectedEntrances = @(
    $developerOnlyEntrances |
        Where-Object { $publicMethodNames -contains $_ }
)
if ($unexpectedEntrances.Count -gt 0)
{
    throw "Release assembly exposes developer-only public methods: $($unexpectedEntrances -join ', ')"
}

$mainViewModel = $assembly.GetType('DDRGpScoreViewer.ViewModels.MainViewModel', $true)
foreach ($methodName in @(
        'StartConfiguredContinuousCaptureAndSaveAsync'
        'StartContinuousCaptureAndSaveAsync'
        'StopContinuousCaptureAsync'))
{
    $method = $mainViewModel.GetMethods($publicMethodFlags) |
        Where-Object { $_.Name -eq $methodName } |
        Select-Object -First 1
    if ($null -eq $method)
    {
        throw "Release assembly is missing the normal monitoring method: $methodName"
    }
}

$mainWindow = $assembly.GetType('DDRGpScoreViewer.MainWindow', $true)
foreach ($methodName in @('StartMonitoringFromTrayAsync', 'StopMonitoringAsync'))
{
    $method = $mainWindow.GetMethods($nonPublicInstanceMethodFlags) |
        Where-Object { $_.Name -eq $methodName } |
        Select-Object -First 1
    if ($null -eq $method)
    {
        throw "Release assembly is missing the normal window monitoring method: $methodName"
    }
}

$assemblyBytes = [System.IO.File]::ReadAllBytes($resolvedAssemblyPath)
$assemblyAsUtf8 = [System.Text.Encoding]::UTF8.GetString($assemblyBytes)
$assemblyAsUtf16 = [System.Text.Encoding]::Unicode.GetString($assemblyBytes)
foreach ($developerOnlyLabel in @(
        '1フレーム取得'
        '連続取得を開始'
        '単発保存'
        'Debug build / 開発者向け操作'))
{
    if ($assemblyAsUtf8.Contains($developerOnlyLabel) -or
        $assemblyAsUtf16.Contains($developerOnlyLabel))
    {
        throw "Release assembly contains a developer-only label: $developerOnlyLabel"
    }
}

Write-Output "Release build verification passed: $resolvedAssemblyPath"
