using System.Diagnostics;
using System.Text.Json;
using Gs66Control.Core;
using Gs66Control.Windows;

static class Curves
{
    internal static readonly string Helper=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"gs66-control-validation","run-20261016-thermal-v9","package","helper","Gs66Control.Helper.exe");
    internal static string DirectoryFor(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Invalid curve request id");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GS66Control","curve-requests",id);
    }
    internal static ConsoleProfile Target(FanCurveControlBaseline before,string name)
    {
        if(!before.Cpu.Select(x=>x.Temperature).SequenceEqual(new[]{0,55,60,65,70,90})||!before.Gpu.Select(x=>x.Temperature).SequenceEqual(new[]{0,50,60,70,82,90}))throw new InvalidDataException("Device temperature breakpoints differ from the reviewed experiment");
        var cpu=name switch{"stock-curve"=>before.Cpu.Select(x=>x.Speed).ToArray(),"curve-a"=>new[]{30,40,48,55,65,85},"curve-b"=>new[]{30,35,42,50,60,85},_=>throw new InvalidDataException("Unknown fixed curve candidate")};
        var gpu=name switch{"stock-curve"=>before.Gpu.Select(x=>x.Speed).ToArray(),"curve-a"=>new[]{0,35,45,55,80,85},_=>new[]{0,30,40,50,80,85}};
        var target=new ConsoleProfile(name,before.Cpu.Select((p,i)=>p with{Speed=cpu[i]}).ToArray(),before.Gpu.Select((p,i)=>p with{Speed=gpu[i]}).ToArray());
        FanCurveControlPolicy.Require(before,target.Cpu,target.Gpu,false,DateTimeOffset.UtcNow);return target;
    }
    internal static void Heartbeat(string id)
    {
        var path=Path.Combine(DirectoryFor(id),"heartbeat");var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temporary,DateTimeOffset.UtcNow.ToString("O"));File.Move(temporary,path,true);
    }
    internal static Process Start(string action,string id)
    {
        if(action is not("curve-worker" or "curve-recover"))throw new InvalidDataException("Unsupported fixed helper action");
        var info=new ProcessStartInfo(Helper){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(action);info.ArgumentList.Add(id);return Process.Start(info)??throw new IOException("Curve helper failed to start");
    }
    internal static async Task<Process> Apply(Recovery.Journal journal,string name)
    {
        var id=journal.CurveRequestId!;var directory=DirectoryFor(id);Directory.CreateDirectory(directory);
        var fresh=new WindowsCurveControlBackend().CaptureBaseline();
        if(journal.CurveBefore is null||!Same(journal.CurveBefore,fresh)||fresh.Mode!=13)throw new InvalidOperationException("Office experiments require an unchanged original automatic-mode baseline");
        var target=Target(fresh,name);
        Recovery.Write(Path.Combine(directory,"request.json"),new{Target=target,ParentPid=journal.ParentPid,ParentStartUtc=journal.ParentStartUtc,RequestId=id,IsTrial=false,TrialSeconds=5,ExpectedBaseline=fresh});
        Heartbeat(id);var worker=Start("curve-worker",id);var deadline=DateTime.UtcNow.AddSeconds(30);
        while(DateTime.UtcNow<deadline)
        {
            var status=Path.Combine(directory,"status.json");
            if(File.Exists(Path.Combine(directory,"result.json"))||worker.HasExited)throw new IOException("Curve worker ended before the active state; inspect its journal");
            if(File.Exists(status))
            {
                using var json=JsonDocument.Parse(File.ReadAllText(status));
                if(json.RootElement.GetProperty("phase").GetString()=="Active")
                {
                    var actual=new WindowsCurveControlBackend().CaptureBaseline();
                    if(actual.Mode!=141||!actual.Cpu.SequenceEqual(target.Cpu)||!actual.Gpu.SequenceEqual(target.Gpu))throw new IOException("Independent active-curve read-back differs from its target");
                    return worker;
                }
            }
            await Task.Delay(200);
        }
        throw new TimeoutException("Curve startup deadline reached; independent recovery must stop the owned session");
    }
    internal static void RequestStop(string id)
    {
        var directory=DirectoryFor(id);if(Directory.Exists(directory))Recovery.Write(Path.Combine(directory,"stop"),new{Reason="Bounded laboratory session complete or parent lost"});
    }
    internal static bool Same(FanCurveControlBaseline a,FanCurveControlBaseline b)=>!a.IsSimulated&&!b.IsSimulated&&a.Mode==b.Mode&&a.Source==b.Source&&a.Cpu.SequenceEqual(b.Cpu)&&a.Gpu.SequenceEqual(b.Gpu);
    internal static bool Restore(Recovery.Journal journal)
    {
        if(journal.CurveRequestId is not string id)return true;
        var directory=DirectoryFor(id);if(!File.Exists(Path.Combine(directory,"journal.json")))return true;
        RequestStop(id);var deadline=DateTime.UtcNow.AddSeconds(20);var result=Path.Combine(directory,"result.json");
        while(DateTime.UtcNow<deadline&&!File.Exists(result))Thread.Sleep(200);
        if(File.Exists(result))
        {
            using var json=JsonDocument.Parse(File.ReadAllText(result));
            if(json.RootElement.GetProperty("requestId").GetString()==id&&json.RootElement.GetProperty("recoveryVerified").GetBoolean()&&
               journal.CurveBefore is not null&&Same(journal.CurveBefore,new WindowsCurveControlBackend().CaptureBaseline()))return true;
        }
        using var recovery=Start("curve-recover",id);
        if(!recovery.WaitForExit(35000))throw new TimeoutException("Independent curve recovery remains active; it was not terminated");
        if(recovery.ExitCode!=0)throw new IOException("Curve recovery was not confirmed; preserve the diagnostic journal");
        return journal.CurveBefore is not null&&Same(journal.CurveBefore,new WindowsCurveControlBackend().CaptureBaseline());
    }
}
