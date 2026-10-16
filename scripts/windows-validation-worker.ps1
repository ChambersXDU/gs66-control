param(
    [Parameter(Mandatory=$true)][string]$WorkerScript,
    [Parameter(Mandatory=$true)][string]$JobFolder,
    [Parameter(Mandatory=$true)][string]$TaskName
)
$ErrorActionPreference='Stop'
$started=[DateTime]::UtcNow.ToString('o')
function Save-Status($state,$code,$message) {
    $temporary=Join-Path $JobFolder 'status.tmp'
    @{State=$state;StartedUtc=$started;UpdatedUtc=[DateTime]::UtcNow.ToString('o');ExitCode=$code;Error=$message} | ConvertTo-Json | Set-Content -Encoding UTF8 $temporary
    Move-Item -LiteralPath $temporary -Destination (Join-Path $JobFolder 'status.json') -Force
}
try {
    Save-Status 'Running' $null $null
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName="$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
    $start.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$WorkerScript+'"'
    $start.UseShellExecute=$false
    $start.RedirectStandardOutput=$true
    $start.RedirectStandardError=$true
    $start.StandardOutputEncoding=[Text.Encoding]::UTF8
    $start.StandardErrorEncoding=[Text.Encoding]::UTF8
    $process=[Diagnostics.Process]::Start($start)
    $output=$process.StandardOutput.ReadToEndAsync()
    $errors=$process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $code=$process.ExitCode
    $output.GetAwaiter().GetResult() | Set-Content -Encoding UTF8 (Join-Path $JobFolder 'stdout.log')
    $errors.GetAwaiter().GetResult() | Set-Content -Encoding UTF8 (Join-Path $JobFolder 'stderr.log')
    $process.Dispose()
    if ($code -eq 0) { Save-Status 'Succeeded' $code $null } else { Save-Status 'Failed' $code 'Inspect stdout.log and stderr.log.' }
} catch {
    Save-Status 'Failed' $null $_.Exception.Message
} finally {
    try { Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop }
    catch { $_.Exception.Message | Set-Content -Encoding UTF8 (Join-Path $JobFolder 'cleanup-error.log') }
}
