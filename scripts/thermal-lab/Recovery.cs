using System.Diagnostics;
using System.Text.Json;
using Gs66Control.Windows;
using Gs66Control.Core;

static class Recovery
{
    internal static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"gs66-control-validation","thermal-20261016");
    internal static string Folder(string job)
    {
        if(job.Length is <1 or >64||job.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c=='-')))throw new InvalidDataException("Invalid job name");
        return Path.Combine(Root,job);
    }
    internal sealed record Journal(CpuPolicySnapshot Before,CpuPolicyValues Target,int ParentPid,DateTime ParentStartUtc,DateTimeOffset Deadline,string Nonce,
        string? CurveRequestId=null,FanCurveControlBaseline? CurveBefore=null);
    internal static void Write(string path,object value)
    {
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,path,true);
    }
    internal static Journal Read(string folder)
    {
        var path=Path.Combine(folder,"cpu-journal.json");
        if(new FileInfo(path).Length>16384)throw new InvalidDataException("Oversized recovery journal");
        var journal=JsonSerializer.Deserialize<Journal>(File.ReadAllText(path))??throw new InvalidDataException("Missing journal");
        journal.Before.Ac.ValidateTarget();journal.Target.ValidateTarget();
        if(journal.CurveRequestId is string curveId && (!Guid.TryParseExact(curveId,"N",out _)||journal.CurveBefore is null||journal.CurveBefore.IsSimulated||journal.CurveBefore.Mode!=13))
            throw new InvalidDataException("Invalid curve recovery binding");
        if(journal.Before.IsSimulated||journal.ParentPid<=0||!Guid.TryParseExact(journal.Nonce,"N",out _)||
           journal.Deadline<DateTimeOffset.UtcNow.AddMinutes(-10)||journal.Deadline>DateTimeOffset.UtcNow.AddMinutes(10))throw new InvalidDataException("Invalid recovery journal");
        return journal;
    }
    internal static bool Restore(Journal journal)
    {
        if(journal.CurveRequestId is string id)Curves.RequestStop(id);
        bool cpu=false,curve=false;var errors=new List<Exception>();
        try{cpu=RestoreCpu(journal);}catch(Exception ex){errors.Add(ex);}
        try{curve=Curves.Restore(journal);}catch(Exception ex){errors.Add(ex);}
        if(errors.Count!=0)throw new AggregateException("Laboratory restoration was not confirmed",errors);
        return cpu&&curve;
    }
    private static bool RestoreCpu(Journal journal)
    {
        var backend=new WindowsCpuPolicyBackend();var live=backend.Capture();
        if(live.Scheme!=journal.Before.Scheme)throw new InvalidOperationException("External active power-plan change preserved");
        foreach(var setting in new[]{CpuPolicySetting.EnergyPreference,CpuPolicySetting.BoostMode,CpuPolicySetting.MaximumFrequency,CpuPolicySetting.MaximumState})
        {
            static int Value(CpuPolicyValues v,CpuPolicySetting s)=>s switch{
                CpuPolicySetting.EnergyPreference=>v.EnergyPreference,CpuPolicySetting.BoostMode=>v.BoostMode,
                CpuPolicySetting.MaximumFrequency=>v.MaximumFrequencyMhz,_=>v.MaximumStatePercent};
            var value=Value(live.Ac,setting);
            if(value!=Value(journal.Target,setting)&&value!=Value(journal.Before.Ac,setting))throw new InvalidOperationException("Unknown external CPU policy mutation preserved");
        }
        backend.Apply(live,journal.Before.Ac,true);
        return WindowsCpuPolicyBackend.SamePolicy(journal.Before,backend.Capture());
    }
    internal static async Task<int> Watch(string job)
    {
        var folder=Folder(job);var journal=Read(folder);Write(Path.Combine(folder,"recovery-ready.json"),new{journal.Nonce,ProcessId=Environment.ProcessId});
        string reason="deadline";
        while(DateTimeOffset.UtcNow<journal.Deadline)
        {
            if(File.Exists(Path.Combine(folder,"complete.json")))return 0;
            bool alive=false;
            try{using var parent=Process.GetProcessById(journal.ParentPid);alive=parent.StartTime.ToUniversalTime()==journal.ParentStartUtc&&!parent.HasExited;}catch(ArgumentException){}
            if(!alive){reason="parent-ended";break;}
            var heartbeat=Path.Combine(folder,"heartbeat");
            if(!File.Exists(heartbeat)||DateTime.UtcNow-File.GetLastWriteTimeUtc(heartbeat)>TimeSpan.FromSeconds(6)){reason="heartbeat-expired";break;}
            await Task.Delay(500);
        }
        for(var attempt=0;attempt<5;attempt++)
        {
            try{var verified=Restore(journal);Write(Path.Combine(folder,"independent-recovery.json"),new{Verified=verified,Reason=reason,Attempt=attempt+1,CompletedUtc=DateTimeOffset.UtcNow});return verified?0:2;}
            catch(Exception ex){Write(Path.Combine(folder,"independent-recovery.json"),new{Verified=false,Reason=reason,Error=ex.Message,Attempt=attempt+1});await Task.Delay(1000);}
        }
        return 2;
    }
}
