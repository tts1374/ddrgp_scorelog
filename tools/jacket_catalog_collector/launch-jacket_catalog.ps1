$ErrorActionPreference = "Stop"

$isAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdministrator) {
    Start-Process powershell.exe `
        -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" `
        -Verb RunAs
    exit
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$projectPath = Join-Path $PSScriptRoot "src\JacketCatalogCollector\JacketCatalogCollector.csproj"

Set-Location -LiteralPath $repositoryRoot
dotnet run --project $projectPath
exit $LASTEXITCODE
