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
$companyAttribute = $assembly.GetCustomAttributes([System.Reflection.AssemblyCompanyAttribute], $false) |
    Select-Object -First 1
$productAttribute = $assembly.GetCustomAttributes([System.Reflection.AssemblyProductAttribute], $false) |
    Select-Object -First 1
if ($null -eq $companyAttribute -or $companyAttribute.Company -ne '2ten.')
{
    throw "Release assembly company metadata must be 2ten., found: $($companyAttribute.Company)"
}
if ($null -eq $productAttribute -or $productAttribute.Product -ne 'GP Score Log')
{
    throw "Release assembly product metadata must be GP Score Log, found: $($productAttribute.Product)"
}
$executablePath = Join-Path (Split-Path -Parent $resolvedAssemblyPath) 'DDRGpScoreViewer.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf))
{
    throw "Release executable was not found: $executablePath"
}
$executableVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
if ($executableVersionInfo.CompanyName -ne '2ten.' -or
    $executableVersionInfo.ProductName -ne 'GP Score Log')
{
    throw "Release executable metadata mismatch: company=$($executableVersionInfo.CompanyName); product=$($executableVersionInfo.ProductName)"
}
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

foreach ($forbiddenRuntimeMarker in @(
        'tools\vision_poc'
        'tools/vision_poc'
        'DDRGP_PYTHON'
        'PythonLiveResultAnalyzer'
        'PythonCaptureSaveWorkflowRunner'
        'PythonPersonalScoreDbWorkflowRunner'
        'Tesseract'
        'tesseract'))
{
    if ($assemblyAsUtf8.IndexOf($forbiddenRuntimeMarker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $assemblyAsUtf16.IndexOf($forbiddenRuntimeMarker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
    {
        throw "Release assembly contains a forbidden external runtime marker: $forbiddenRuntimeMarker"
    }
}

Write-Output "Release build verification passed: $resolvedAssemblyPath"
