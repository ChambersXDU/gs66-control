param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Gs66AcpiTables {
    [DllImport("kernel32.dll",SetLastError=true)] public static extern uint EnumSystemFirmwareTables(uint provider,byte[] buffer,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] public static extern uint GetSystemFirmwareTable(uint provider,uint id,byte[] buffer,uint size);
}
'@
$provider=[uint32]0x41435049
$size=[Gs66AcpiTables]::EnumSystemFirmwareTables($provider,$null,0)
if($size -eq 0 -or $size -gt 65536 -or $size % 4 -ne 0){throw 'Invalid ACPI table enumeration size.'}
$ids=New-Object byte[] $size
if([Gs66AcpiTables]::EnumSystemFirmwareTables($provider,$ids,$size) -ne $size){throw 'ACPI table enumeration changed.'}
$counts=@{}
for($i=0;$i -lt $size;$i+=4){$name=[Text.Encoding]::ASCII.GetString($ids,$i,4);if(!$counts.ContainsKey($name)){$counts[$name]=0};$counts[$name]++}
$captured=@()
foreach($name in @('DSDT','SSDT')) {
 if(!$counts.ContainsKey($name)){continue}
 $id=[BitConverter]::ToUInt32([Text.Encoding]::ASCII.GetBytes($name),0)
 $length=[Gs66AcpiTables]::GetSystemFirmwareTable($provider,$id,$null,0)
 if($length -lt 36 -or $length -gt 4MB){throw 'Invalid AML table size.'}
 $bytes=New-Object byte[] $length
 if([Gs66AcpiTables]::GetSystemFirmwareTable($provider,$id,$bytes,$length) -ne $length){throw 'AML table length changed.'}
 if([Text.Encoding]::ASCII.GetString($bytes,0,4) -ne $name -or [BitConverter]::ToUInt32($bytes,4) -ne $length){throw 'AML header mismatch.'}
 $checksum=0;foreach($b in $bytes){$checksum=($checksum+$b) -band 255};if($checksum -ne 0){throw 'AML checksum mismatch.'}
 $file=Join-Path $OutputDirectory ($name.ToLowerInvariant()+'.dat')
 [IO.File]::WriteAllBytes($file,$bytes)
 $captured+=@{Table=$name;Bytes=$length;Sha256=(Get-FileHash $file -Algorithm SHA256).Hash;EnumeratedCopies=$counts[$name];CapturedCopies=1}
}
if(!(@($captured | Where-Object {$_.Table -eq 'DSDT'}).Count)) {
 $base='Registry::HKEY_LOCAL_MACHINE\HARDWARE\ACPI\DSDT'
 if(Test-Path -LiteralPath $base) {
  $keys=@(Get-Item -LiteralPath $base)+@(Get-ChildItem -LiteralPath $base -Recurse)
  if($keys.Count -gt 128){throw 'ACPI DSDT registry tree is unexpectedly large.'}
  $found=$false
  foreach($key in $keys) {
   if($found){break}
   foreach($name in $key.GetValueNames()) {
    $bytes=$key.GetValue($name)
    if($bytes -isnot [byte[]] -or $bytes.Length -lt 36 -or $bytes.Length -gt 4MB){continue}
    if([Text.Encoding]::ASCII.GetString($bytes,0,4) -ne 'DSDT' -or [BitConverter]::ToUInt32($bytes,4) -ne $bytes.Length){continue}
    $sum=0;foreach($b in $bytes){$sum=($sum+$b) -band 255};if($sum -ne 0){throw 'ACPI DSDT registry checksum mismatch.'}
    $file=Join-Path $OutputDirectory 'dsdt.dat'
    [IO.File]::WriteAllBytes($file,$bytes)
    $captured+=@{Table='DSDT';Bytes=$bytes.Length;Sha256=(Get-FileHash $file -Algorithm SHA256).Hash;CapturedCopies=1;Source='Volatile Windows HARDWARE/ACPI/DSDT registry, read-only'}
    $found=$true;break
   }
  }
 }
}
@{Source='Windows firmware-table API, with read-only volatile DSDT registry fallback';Captured=$captured;Limitation='Only first SSDT is available through the API; no AML execution, driver, SPI access or licensing tables.'} | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $OutputDirectory 'manifest.json')
Get-Content (Join-Path $OutputDirectory 'manifest.json')
