param([switch]$Launch)
$ErrorActionPreference = 'Stop'
$package = $PSScriptRoot
$manifest = Get-Content -Raw -LiteralPath (Join-Path $package 'release-manifest.json') | ConvertFrom-Json
foreach ($entry in $manifest.PSObject.Properties) {
    if ($entry.Name -match '(^|/)\.\.(/|$)' -or [IO.Path]::IsPathRooted($entry.Name)) { throw 'Invalid manifest path.' }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $package $entry.Name)).Hash.ToLowerInvariant() -ne $entry.Value) {
        throw ('Release hash mismatch: ' + $entry.Name)
    }
}
$root = Join-Path $env:LOCALAPPDATA 'GS66Control'
New-Item -ItemType Directory -Force -Path $root | Out-Null
$version = 'app-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$staging = Join-Path $root ($version + '.staging')
$destination = Join-Path $root $version
New-Item -ItemType Directory -Path $staging | Out-Null
foreach ($entry in $manifest.PSObject.Properties) {
    $target = Join-Path $staging $entry.Name
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $package $entry.Name) -Destination $target
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash.ToLowerInvariant() -ne $entry.Value) {
        throw ('Installed file hash mismatch: ' + $entry.Name)
    }
}
Copy-Item -LiteralPath (Join-Path $package 'release-manifest.json') -Destination $staging
[IO.Directory]::Move($staging, $destination)
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'GS66 Control.lnk'))
$shortcut.TargetPath = Join-Path $destination 'Gs66Control.Desktop.exe'
$shortcut.WorkingDirectory = $destination
$shortcut.IconLocation = $shortcut.TargetPath + ',0'
$shortcut.Save()
Write-Output ('Installed: ' + $shortcut.TargetPath)
if ($Launch) { Start-Process -FilePath $shortcut.TargetPath }
