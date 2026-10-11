param(
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot "artifacts/windows-x64"
}

dotnet publish (Join-Path $projectRoot "src/Gs66Control.App/Gs66Control.App.csproj") `
    --configuration Release --runtime $Runtime --self-contained true `
    -p:EnableWindowsTargeting=true --output $OutputDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Windows app publish failed (exit code $LASTEXITCODE)."
}
Write-Host "Published Windows app to $OutputDirectory"
