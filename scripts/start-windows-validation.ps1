param(
    [Parameter(Mandatory=$true)][string]$WorkerScript,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,63}$')][string]$JobName
)
$ErrorActionPreference='Stop'
$root=Join-Path $env:LOCALAPPDATA 'gs66-control-validation'
$scriptPath=(Resolve-Path -LiteralPath $WorkerScript).Path
if (!$scriptPath.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or $scriptPath.Contains('"')) {
    throw 'Validation script must be inside the local validation workspace.'
}
$folder=Join-Path $root ('jobs\'+$JobName)
if (Test-Path -LiteralPath $folder) { throw 'Job name already exists; inspect its status instead of dispatching twice.' }
New-Item -ItemType Directory -Path $folder | Out-Null
$bootstrap=Join-Path $folder 'worker.ps1'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'windows-validation-worker.ps1') -Destination $bootstrap
$taskName='GS66Control-Validation-'+$JobName
$status=Join-Path $folder 'status.json'
@{State='Queued';TaskName=$taskName;StartedUtc=$null;ExitCode=$null} | ConvertTo-Json | Set-Content -Encoding UTF8 $status
$action=New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument ('-NoProfile -ExecutionPolicy Bypass -File "'+$bootstrap+'" -WorkerScript "'+$scriptPath+'" -JobFolder "'+$folder+'" -TaskName "'+$taskName+'"')
$principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$registered=$false
try {
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Description 'One-shot GS66 build/read-only validation; no hardware writes.' | Out-Null
    $registered=$true
    # SSH may carry an elevated token. Give the same user's limited task token
    # permission to delete this exact task after completion.
    $scheduler=New-Object -ComObject 'Schedule.Service'
    $scheduler.Connect()
    $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $scheduler.GetFolder('\').GetTask($taskName).SetSecurityDescriptor(('D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;'+$sid+')'),0)
    Start-ScheduledTask -TaskName $taskName
    [pscustomobject]@{Job=$JobName;StatusFile=$status;LogFile=(Join-Path $folder 'stdout.log')} | ConvertTo-Json
} catch {
    @{State='DispatchFailed';Error=$_.Exception.Message;ExitCode=$null} | ConvertTo-Json | Set-Content -Encoding UTF8 $status
    if ($registered) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue }
    throw
}
