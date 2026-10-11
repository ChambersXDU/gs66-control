# KernCoreLib64.dll: focused Ghidra evidence

This is a preserved excerpt of the offline Ghidra 12.1.4 decompilation of the Dragon Center 2.0.158.0 native access DLL. The input was read from `/tmp/gs66-research/nbfoundation/app/KernCoreLib64.dll`; the DLL was not loaded by Windows and no driver or hardware access was performed.

| Field | Value |
|---|---|
| SHA-256 | `dafc2c87b674e3b7f1fb355064061ad48fb33c79c0eb172390b04e1261e8bf0b` |
| PE | PE32+ x86-64 DLL |
| Analysis project | `/tmp/gs66-research/ghidra-project/KernCore` |
| Decompiler output | `/tmp/gs66-research/kerncore-evidence.txt` |
| Ghidra image base | `0x180000000` |

## `InstallKernCoreLibDriver`, `0x180001150`

```c
ulonglong InstallKernCoreLibDriver(LPCWSTR param_1, byte param_2)
{
  BOOL BVar1;
  SC_HANDLE hSCManager;
  SC_HANDLE hSCObject;

  RemoveKernCoreLibDriver();
  hSCManager = OpenSCManagerW((LPCWSTR)0x0,(LPCWSTR)0x0,0xf003f);
  if (hSCManager != (SC_HANDLE)0x0) {
    hSCObject = CreateServiceW(hSCManager,L"WINIO",L"WINIO",0xf01ff,1,
                               (uint)param_2 * 2 + 1,1,param_1,
                               (LPCWSTR)0x0,(LPDWORD)0x0,(LPCWSTR)0x0,
                               (LPCWSTR)0x0,(LPCWSTR)0x0);
    ...
  }
}
```

The binary evidence shows the service name `WINIO`, a kernel-driver service type (`1`), and the caller-supplied driver path. The observed initializer passes `param_2 = 1`, making the decompiled start-type expression equal to `3` (demand start). The service-creation call itself does not identify an EC register or an MSI-specific protocol.

## `GetPhysLong`, `0x180001590`

```c
ulonglong GetPhysLong(ulonglong param_1,uint *param_2)
{
  ...
  local_38 = param_1;
  if (DAT_1800121c1 == '\0') {
    local_38 = param_1 & 0xffffffff;
  }
  local_40 = 4;
  BVar1 = DeviceIoControl(DAT_180012000,0x80102040,
                          &local_40,0x28,&local_40,0x28,
                          local_48,(LPOVERLAPPED)0x0);
  ...
  *param_2 = *local_28;
  if (DAT_1800121c0 != '\0') {
    BVar1 = DeviceIoControl(DAT_180012000,0x80102044,
                            &local_40,0x28,(LPVOID)0x0,0,
                            local_48,(LPOVERLAPPED)0x0);
  }
  ...
}
```

The request is a 0x28-byte map descriptor with a requested width of 4. `0x80102040` maps physical memory and `0x80102044` unmaps it; the mapped pointer is dereferenced as a 32-bit value. The decompiler's fallback assignment on map failure is preserved in the full output and should not be interpreted as a successful hardware reading.

## `MapPhysToLin`, `0x1800016b0`

```c
undefined8 MapPhysToLin(LPVOID param_1)
{
  DWORD local_res10[6];
  if (DAT_1800121c0 != '\0') {
    BVar1 = DeviceIoControl(DAT_180012000,0x80102040,
                            param_1,0x28,param_1,0x28,
                            local_res10,(LPOVERLAPPED)0x0);
    if (BVar1 != 0) {
      return *(undefined8 *)((longlong)param_1 + 0x18);
    }
  }
  return 0;
}
```

The returned pointer is descriptor offset `0x18`; this is a generic map helper, not proof of a particular EC layout.

## `SetPhysLong`, `0x180001720`

```c
ulonglong SetPhysLong(ulonglong param_1,undefined4 param_2)
{
  ...
  local_38 = param_1;
  if (DAT_1800121c1 == '\0') {
    local_38 = param_1 & 0xffffffff;
  }
  local_40 = 4;
  BVar1 = DeviceIoControl(DAT_180012000,0x80102040,
                          &local_40,0x28,&local_40,0x28,
                          local_48,(LPOVERLAPPED)0x0);
  if ((BVar1 != 0) && (local_28 != (undefined4 *)0x0)) {
    *local_28 = param_2;
    ...
    BVar1 = DeviceIoControl(DAT_180012000,0x80102044,
                            &local_40,0x28,(LPVOID)0x0,0,
                            local_48,(LPOVERLAPPED)0x0);
    return ...;
  }
  return ...;
}
```

This writes a caller-provided 32-bit value through the same map/unmap boundary. No GS66-specific address appears in this function.

## `InitializeKernCoreLib`, `0x180001a30`

The confirmed function entry is `0x180001a30` (not `0x18001a30` without the zero). The decompiler shows:

```c
DAT_1800121c1 = '\x01';
hDevice = CreateFileW(L"\\\\.\\\\WINIO",0xc0000000,0,
                      (LPSECURITY_ATTRIBUTES)0x0,3,0x80,(HANDLE)0x0);
if (hDevice == &DAT_ffffffffffffffff) {
  ...
  hDevice = (undefined1 *)(*(code *)PTR_InstallKernCoreLibDriver_18000f510)
             (&DAT_1800121d0,1);
  ...
  hDevice = CreateFileW(L"\\\\.\\\\WINIO",0xc0000000,3,
                        (LPSECURITY_ATTRIBUTES)0x0,3,0x80,(HANDLE)0x0);
}
DAT_180012000 = hDevice;
...
DAT_1800121c0 = 1;
```

The function opens `\\.\\WINIO`, derives the module path for the `.Sys` image when the first open fails, invokes the install/start path, and retries. The 64-bit/32-bit conditional branches are represented by globals in the compiler output; the exact runtime branch should be checked against the managed caller and Windows execution state.

## `UnmapPhysicalMemory`, `0x180001820`

```c
ulonglong UnmapPhysicalMemory(LPVOID param_1)
{
  if (DAT_1800121c0 != '\0') {
    BVar1 = DeviceIoControl(DAT_180012000,0x80102044,
                            param_1,0x28,(LPVOID)0x0,0,
                            local_res10,(LPOVERLAPPED)0x0);
    if (BVar1 != 0) return ...;
  }
  return ...;
}
```

Full unabridged output is retained in `/tmp/gs66-research/kerncore-evidence.txt`.
