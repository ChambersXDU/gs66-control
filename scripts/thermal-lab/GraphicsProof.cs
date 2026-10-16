using System.Runtime.InteropServices;
using Gs66Control.Windows;

static class GraphicsProof
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int EnumAdapters(IntPtr factory,uint index,out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetDescription(IntPtr adapter,out Description description);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Description
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Text;
        public uint Vendor,Device,Subsystem,Revision;public UIntPtr DedicatedVideo,DedicatedSystem,Shared;public long Luid;public uint Flags;
    }
    private static T Method<T>(IntPtr pointer,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(pointer),slot*IntPtr.Size));
    [DllImport("dxgi.dll",ExactSpelling=true)]private static extern int CreateDXGIFactory1(ref Guid iid,out IntPtr factory);
    [DllImport("d3d11.dll",ExactSpelling=true)]private static extern int D3D11CreateDevice(IntPtr adapter,uint driver,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out uint selected,out IntPtr context);
    internal static async Task<int> Run(string job)
    {
        var folder=Recovery.Folder(job);Directory.CreateDirectory(folder);
        var probe=new WindowsGpuPowerProbe();var rows=new List<object>();
        var initial=probe.Capture();rows.Add(new{Phase="before",Evidence=initial});
        IntPtr factory=IntPtr.Zero,selectedAdapter=IntPtr.Zero,device=IntPtr.Zero,context=IntPtr.Zero;
        var d0=false;var d3=false;
        try
        {
            var iid=new Guid("770aae78-f26f-4dba-a829-253c83d1b387");Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid,out factory));
            for(uint i=0;i<16;i++)
            {
                var hr=Method<EnumAdapters>(factory,12)(factory,i,out var adapter);if(hr==unchecked((int)0x887A0002))break;Marshal.ThrowExceptionForHR(hr);
                try{Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter,10)(adapter,out var description));
                    if(description.Vendor==0x10DE&&description.Text=="NVIDIA GeForce RTX 2060"){selectedAdapter=adapter;adapter=IntPtr.Zero;break;}}
                finally{if(adapter!=IntPtr.Zero)Marshal.Release(adapter);}
            }
            if(selectedAdapter==IntPtr.Zero)throw new InvalidDataException("Reviewed NVIDIA graphics adapter missing");
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(selectedAdapter,0,IntPtr.Zero,0,IntPtr.Zero,0,7,out device,out _,out context));
            for(var i=0;i<30;i++){var evidence=probe.Capture();d0|=evidence.DevicePowerState==1;rows.Add(new{Phase="graphics-context",Evidence=evidence});await Task.Delay(200);}
        }
        finally{if(context!=IntPtr.Zero)Marshal.Release(context);if(device!=IntPtr.Zero)Marshal.Release(device);if(selectedAdapter!=IntPtr.Zero)Marshal.Release(selectedAdapter);if(factory!=IntPtr.Zero)Marshal.Release(factory);}
        for(var i=0;i<30;i++){var evidence=probe.Capture();d3|=evidence.DevicePowerState==4;rows.Add(new{Phase="released",Evidence=evidence});await Task.Delay(500);}
        var passed=initial.DevicePowerState==4&&d0&&d3;
        Recovery.Write(Path.Combine(folder,"gpu-power-proof.json"),new{Passed=passed,InitialD3=initial.DevicePowerState==4,ObservedD0=d0,ReturnedD3=d3,IsSimulated=false,HardwareControlWrites=0,Workload="One D3D11 device/context, six seconds; no rendering loop",Rows=rows});
        return passed?0:2;
    }
}
