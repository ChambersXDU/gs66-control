param([string]$OutputFolder, [switch]$ExternalSensorReader)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$package = $PSScriptRoot
if (-not $OutputFolder) {
    $OutputFolder = Join-Path $env:LOCALAPPDATA ('gs66-control-validation\release-' + [Guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $OutputFolder) { throw 'Validation output already exists.' }
New-Item -ItemType Directory -Path $OutputFolder | Out-Null
$manifest = Get-Content -Raw -LiteralPath (Join-Path $package 'release-manifest.json') | ConvertFrom-Json
foreach ($entry in $manifest.PSObject.Properties) {
    if ($entry.Name -match '(^|/)\.\.(/|$)' -or [IO.Path]::IsPathRooted($entry.Name)) { throw 'Invalid manifest path.' }
    $path = Join-Path $package $entry.Name
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() -ne $entry.Value) {
        throw ('Release hash mismatch: ' + $entry.Name)
    }
}
function Invoke-Check([string]$Executable, [string[]]$Arguments, [string]$Name, [int[]]$AllowedCodes = @(0)) {
    $quoted = @($Arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $Executable
    $start.Arguments = $quoted
    $start.UseShellExecute = $false
    $start.CreateNoWindow = ($Name -ne 'desktop-smoke')
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $start.StandardErrorEncoding = [Text.Encoding]::UTF8
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        $deadline = [DateTime]::UtcNow.AddMinutes(20)
        $readerRequested = $false
        while (-not $process.WaitForExit(100)) {
            if ($Name -eq 'desktop-smoke' -and -not $ExternalSensorReader -and -not $readerRequested) {
                $leasePath = Join-Path $Arguments[1] 'sensor-session.json'
                if (Test-Path -LiteralPath $leasePath) {
                    $lease = Get-Content -Raw -LiteralPath $leasePath | ConvertFrom-Json
                    $parsedId = [Guid]::Empty
                    if ($lease.ParentPid -ne $process.Id -or -not [Guid]::TryParseExact($lease.RequestId, 'N', [ref]$parsedId)) {
                        throw 'Sensor reader lease does not match the smoke process.'
                    }
                    $readerRequested = $true
                    $reader = Start-Process -FilePath (Join-Path $package 'helper\Gs66Control.Helper.exe') `
                        -ArgumentList @('fan-reader', $lease.RequestId) -Verb RunAs -WindowStyle Hidden -PassThru
                    $reader.Dispose()
                }
            }
            if ([DateTime]::UtcNow -gt $deadline) { throw ($Name + ' timed out; check the process and logs.') }
        }
        $code = $process.ExitCode
        $output.GetAwaiter().GetResult() | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputFolder ($Name + '.stdout.log'))
        $errors.GetAwaiter().GetResult() | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputFolder ($Name + '.stderr.log'))
    } finally { $process.Dispose() }
    if ($AllowedCodes -notcontains $code) { throw ($Name + ' failed with exit code ' + $code) }
    return $code
}
$helper = Join-Path $package 'helper\Gs66Control.Helper.exe'
$desktop = Join-Path $package 'Gs66Control.Desktop.exe'
$result = @{ Passed = $false; ExplicitHardwareWrites = 0; IsSimulated = $false }
try {
    $null = Invoke-Check $helper @('simulate-curves', (Join-Path $OutputFolder 'curve-simulation')) 'curve-simulation'
    $null = Invoke-Check $helper @('curve-cross-suite', (Join-Path $OutputFolder 'process-simulation')) 'process-simulation'
    $null = Invoke-Check $helper @('simulate-suite', (Join-Path $OutputFolder 'automatic-simulation')) 'automatic-simulation'
    $smoke = Join-Path $OutputFolder 'ui-smoke'
    $null = Invoke-Check $desktop @('--smoke-test', $smoke) 'desktop-smoke'
    $ui = Get-Content -Raw -LiteralPath (Join-Path $smoke 'verification.json') | ConvertFrom-Json
    if ($ui.passed -ne $true -or $ui.explicitHardwareWrites -ne 0 -or $ui.telemetry.isSimulated -ne $false) {
        throw 'Desktop read-only verification was not confirmed.'
    }
    $previewCode = Invoke-Check $helper @('control-preview') 'control-preview' @(0, 2)
    $preview = Get-Content -Raw -LiteralPath (Join-Path $OutputFolder 'control-preview.stdout.log') | ConvertFrom-Json
    if ($preview.IsSimulated -ne $false -or $preview.ExplicitHardwareWrites -ne 0) { throw 'Invalid control preview.' }
    $result.Passed = $true
    $result.ControlEligible = ($previewCode -eq 0 -and $preview.Eligible -eq $true)
    $result.ControlBlockers = $preview.Blockers
    $result.UiSmoke = $true
} catch {
    $result.Error = $_.Exception.Message
    throw
} finally {
    $result | ConvertTo-Json -Depth 12 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputFolder 'verification.json')
    Write-Output ('Read-only validation: ' + $OutputFolder)
}
