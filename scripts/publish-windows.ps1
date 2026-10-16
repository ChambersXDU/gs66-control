param([string]$OutputDirectory = "")
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts/release'
}
python (Join-Path $PSScriptRoot 'package-windows.py') --output $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Windows package build failed ($LASTEXITCODE)." }
