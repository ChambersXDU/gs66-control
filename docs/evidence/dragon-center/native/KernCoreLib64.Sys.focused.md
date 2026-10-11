# KernCoreLib64.Sys: focused Ghidra evidence

This is a preserved excerpt of the offline Ghidra 12.1.4 decompilation of the kernel driver shipped with Dragon Center 2.0.158.0. The file was parsed as a PE image only; it was never installed, loaded, or executed.

| Field | Value |
|---|---|
| SHA-256 | `a3c71d0d687f41ebf3970e7e799cd8c4784557ac50d9728b62f4273f9627cffc` |
| PE | PE32+ x86-64 kernel driver image |
| Analysis project | `/tmp/gs66-research/ghidra-sys-project/KernCoreSys` |
| Decompiler output | `/tmp/gs66-research/kerncore-sys-all.txt` |
| Ghidra image base | `0x140000000` |

## Driver initialization: `FUN_140001000`, `0x140001000`

```c
int FUN_140001000(longlong param_1)
{
  int iVar1;
  undefined8 local_res18[2];
  undefined1 local_28[16];
  undefined1 local_18[16];

  local_res18[0] = 0;
  RtlInitUnicodeString(local_28,L"\\Device\\WinIo");
  iVar1 = IoCreateDevice(param_1,0,local_28,0x8010,0,0,local_res18);
  if (-1 < iVar1) {
    *(code **)(param_1 + 0xe0) = FUN_140001300;
    *(code **)(param_1 + 0x80) = FUN_140001300;
    *(code **)(param_1 + 0x70) = FUN_140001300;
    *(code **)(param_1 + 0x68) = FUN_140001610;
    RtlInitUnicodeString(local_18,L"\\DosDevices\\WinIo");
    iVar1 = IoCreateSymbolicLink(local_18,local_28);
    if (iVar1 < 0) IoDeleteDevice(local_res18[0]);
  }
  return iVar1;
}
```

The offsets correspond to the driver object's create, close, device-control, and unload slots. The device and DOS link names are direct binary evidence.

## Physical-memory mapping: `FUN_1400010c0`, `0x1400010c0`

```c
int FUN_1400010c0(longlong param_1,longlong param_2,
                  longlong *param_3,undefined8 *param_4,
                  undefined8 *param_5)
{
  ...
  RtlInitUnicodeString(local_68,L"\\Device\\PhysicalMemory");
  ...
  iVar3 = ZwOpenSection(param_4,0xf001f,local_58);
  if ((-1 < iVar3) &&
      (iVar3 = ObReferenceObjectByHandle(*param_4,0xf001f,0,0,
                                          param_5,0), -1 < iVar3)) {
    ...
    cVar1 = HalTranslateBusAddress(1,0,param_1,local_res20,&local_80);
    ...
    iVar3 = ZwMapViewOfSection(*param_4,0xffffffffffffffff,&local_70,
                               0,local_res10[0],&local_78,local_res10,
                               1,0,0x204);
    if (iVar3 == -0x3fffffe8) {
      iVar3 = ZwMapViewOfSection(...,1,0,4);
    }
    if (-1 < iVar3) {
      *param_3 = (local_80 - local_78) + local_70;
      return iVar3;
    }
  }
  ZwClose(*param_4);
  return iVar3;
}
```

This is the backing implementation of the user-mode map IOCTL. The physical address is translated and mapped through the Windows physical-memory section. No EC address is embedded in this routine.

## Device-control dispatch: `FUN_140001300`, `0x140001300`

The decompiler identifies `MajorFunction == 0x0e` and these signed constants:

```c
iVar7 = *(int *)(pcVar3 + 0x18);
if (iVar7 == -0x7fefdfc0) {       // 0x80102040
  ...
  iVar7 = FUN_1400010c0(...);
  ...
}
else {
  if (iVar7 != -0x7fefdfbc) {     // 0x80102044
    if (iVar7 == -0x7fefdfb0) {   // 0x80102050
      ...
      if (local_58._6_1_ == '\x01') {
        bVar4 = in((undefined2)local_58);
      }
      else if (local_58._6_1_ == '\x02') {
        uVar5 = in((undefined2)local_58);
      }
      else if (local_58._6_1_ == '\x04') {
        local_58._2_4_ = in((undefined2)local_58);
      }
      ...
    }
    else if ((iVar7 == -0x7fefdfac) && (uVar1 != 0)) { // 0x80102054
      ...
      if (local_58._6_1_ == '\x01') {
        out((undefined2)local_58,local_58._2_1_);
      }
      else if (local_58._6_1_ == '\x02') {
        out((undefined2)local_58,local_58._2_2_);
      }
      else if (local_58._6_1_ == '\x04') {
        out((undefined2)local_58,local_58._2_4_);
      }
    }
    ...
  }
  ...
  iVar7 = FUN_1400012ac(...);     // unmap
}
```

The four IOCTL values and 1/2/4-byte port operations are directly present in the decompiled dispatch routine. They establish a generic WinIO-style physical-memory and I/O-port boundary; they do not establish that Dragon Center uses a particular EC register on GS66 10SE.

## Entry and unload

`entry` at `0x140005000` calls `FUN_140001000`. `FUN_140001610` at `0x140001610` calls `IoDeleteSymbolicLink(L"\\DosDevices\\WinIo")` and deletes the device object. `FUN_1400012ac` at `0x1400012ac` unmaps the view, dereferences the section object, and closes the section handle.

Full unabridged output is retained in `/tmp/gs66-research/kerncore-sys-all.txt`.
