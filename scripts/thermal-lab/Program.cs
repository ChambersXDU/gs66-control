using System.Diagnostics;
using System.IO.Compression;
using System.Management;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Gs66Control.Core;
using Gs66Control.Windows;

var mode=args.ElementAtOrDefault(0)??"";var job=args.ElementAtOrDefault(1)??"";
if(mode=="recover")return await Recovery.Watch(job);
if(mode=="gpu-proof")return await GraphicsProof.Run(job);
var folder=Recovery.Folder(job);
if(mode=="verify-recovery")
{
    var journal=Recovery.Read(folder);var snapshot=new WindowsCpuPolicyBackend().Capture();
    var verified=WindowsCpuPolicyBackend.SamePolicy(journal.Before,snapshot);
    Recovery.Write(Path.Combine(folder,"independent-readback.json"),new{Verified=verified,Snapshot=snapshot,At=DateTimeOffset.UtcNow});
    return verified?0:2;
}
if(Directory.Exists(folder))throw new InvalidDataException("Job already exists; inspect its result instead of replaying it");
Directory.CreateDirectory(folder);
try
{
 if(mode=="snapshot")
 {
  Recovery.Write(Path.Combine(folder,"snapshot.json"),new{Policy=new WindowsCpuPolicyBackend().Capture(),GpuPower=new WindowsGpuPowerProbe().Capture(),Thermal=new WindowsAutoFanBackend().Capture(false)});return 0;
 }
 if(mode is not("permission" or "crash-recovery" or "office" or "video"))throw new InvalidDataException("Unknown fixed laboratory mode");
 if(mode is "office" or "video")
 {
  var coolDeadline=DateTime.UtcNow.AddMinutes(4);var stable=0;
  while(stable<3)
  {
   var thermal=new WindowsAutoFanBackend().Capture(false);
   AutoFanPolicy.Require(thermal,false);
   stable=thermal.CpuTemperature is >=45 and <=52?stable+1:0;
   Recovery.Write(Path.Combine(folder,"cooldown.json"),new{Cpu=thermal.CpuTemperature,Gpu=thermal.GpuTemperature,GpuPower=thermal.GpuPower,StableSamples=stable,TargetCpuMaximum=52,At=DateTimeOffset.UtcNow});
   if(DateTime.UtcNow>coolDeadline)throw new InvalidOperationException("Comparable starting temperature was not reached within the bounded cooldown; no experiment policy or curve write attempted");
   if(stable<3)await Task.Delay(2000);
  }
 }
 var backend=new WindowsCpuPolicyBackend();var before=backend.Capture();before.Ac.ValidateTarget();
 var scenario=args.ElementAtOrDefault(2)??"baseline";
 var permission=mode is "permission" or "crash-recovery";
 var curveName=args.ElementAtOrDefault(3);
 FanCurveControlBaseline? curveBefore=null;
 string? curveId=null;
 if(curveName is not null)
 {
  if(permission)throw new InvalidDataException("Permission/recovery smoke does not accept hardware curve changes");
  curveBefore=new WindowsCurveControlBackend().CaptureBaseline();
  if(curveBefore.Mode!=13)throw new InvalidOperationException("Curve comparisons start and finish in the original automatic mode");
  _=Curves.Target(curveBefore,curveName);curveId=Guid.NewGuid().ToString("N");
 }
 var target=permission?before.Ac with{EnergyPreference=before.Ac.EnergyPreference<100?before.Ac.EnergyPreference+1:99}:scenario switch{
  "baseline"=>before.Ac,"full"=>before.Ac with{MaximumStatePercent=100,MaximumFrequencyMhz=0},
  "responsive"=>new CpuPolicyValues(55,1,0,100),"gentle"=>new CpuPolicyValues(70,1,0,100),
  "cap-3000"=>new CpuPolicyValues(55,1,3000,100),
  _=>throw new InvalidDataException("Unknown reviewed CPU scenario")};
 if(!permission)
 {
  var thermal=new WindowsAutoFanBackend().Capture(false);AutoFanPolicy.Require(thermal,false);
  if(thermal.CpuTemperature>=75||!new ThermalReading(thermal.CpuTemperature,thermal.GpuTemperature,thermal.GpuPower).GpuWithin(70,false,DateTimeOffset.UtcNow))throw new InvalidOperationException("Conservative laboratory admission failed");
  if(new WindowsPerformanceBackend().CaptureCurrent().RawShiftValue!=193)throw new InvalidOperationException("Office comparisons keep the same C1 hardware performance level");
 }
 using var parent=Process.GetCurrentProcess();
 var journal=new Recovery.Journal(before,target,parent.Id,parent.StartTime.ToUniversalTime(),DateTimeOffset.UtcNow.AddSeconds(permission?60:240),Guid.NewGuid().ToString("N"),curveId,curveBefore);
 Recovery.Write(Path.Combine(folder,"cpu-journal.json"),journal);
 void Heartbeat()
 {
  Recovery.Write(Path.Combine(folder,"heartbeat"),new{At=DateTimeOffset.UtcNow});
  if(curveId is string id&&Directory.Exists(Curves.DirectoryFor(id)))Curves.Heartbeat(id);
 }
 Heartbeat();using var pulse=new Timer(_=>{try{Heartbeat();}catch(IOException){}},null,1000,1000);
 var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 if(Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
 start.ArgumentList.Add("recover");start.ArgumentList.Add(job);
 using var watcher=Process.Start(start)??throw new IOException("Independent CPU recovery process failed to start");
 var readyDeadline=DateTime.UtcNow.AddSeconds(10);
 while(!File.Exists(Path.Combine(folder,"recovery-ready.json"))){if(watcher.HasExited||DateTime.UtcNow>readyDeadline)throw new IOException("Independent recovery was not ready; no policy write attempted");await Task.Delay(100);}
 bool applied=false,restored=false;string? failure=null;var samples=0;var timings=new List<double>();Process? curveWorker=null;
 JsonElement? videoProof=null;
 double videoStartPosition=0;
 try
 {
  var fresh=backend.Capture();if(!WindowsCpuPolicyBackend.SamePolicy(before,fresh))throw new InvalidOperationException("Policy baseline changed before trial");
  var active=backend.Apply(fresh,target,true);applied=true;
  Recovery.Write(Path.Combine(folder,"applied.json"),active);
  if(curveName is not null)curveWorker=await Curves.Apply(journal,curveName);
  if(mode=="crash-recovery"){Recovery.Write(Path.Combine(folder,"intentional-parent-exit.json"),new{ExitCode=42,Reason="Bounded one-point EPP change; independent recovery must restore the original policy"});Environment.Exit(42);}
  if(permission)await Task.Delay(2000);
  else
  {
   var identity=await new WindowsWmiProbe().CaptureAsync();var probe=new WindowsTelemetryProbe(identity);
   var payload=JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0,20000).Select(i=>new{Id=i,Title="Office document item "+i,Category=i%17,Text="A deterministic local document used for a short JSON sorting and archive roundtrip."}));
   _=OfficeBatch(payload);await Task.Delay(20000);
   if(mode=="video")
   {
    Recovery.Write(Path.Combine(folder,"video-start.json"),new{Ready=true,At=DateTimeOffset.UtcNow});
    var videoDeadline=DateTime.UtcNow.AddSeconds(25);
    while(true)
    {
     var path=Path.Combine(folder,"video-progress.json");
     if(File.Exists(Path.Combine(folder,"video-error.json")))throw new IOException("Media playback could not start");
     if(File.Exists(path))
     {
      using var data=JsonDocument.Parse(File.ReadAllText(path));
      if(data.RootElement.GetProperty("PlayedSeconds").GetDouble()>1){videoStartPosition=data.RootElement.GetProperty("PlayedSeconds").GetDouble();break;}
     }
     if(DateTime.UtcNow>videoDeadline)throw new TimeoutException("Ordinary-user media playback did not advance");
     await Task.Delay(200);
    }
   }
   var clock=Stopwatch.StartNew();double nextBurst=10;double nextModeCheck=0;
   using var log=new StreamWriter(Path.Combine(folder,"samples.jsonl")){AutoFlush=true};
   while(clock.Elapsed.TotalSeconds<120)
   {
    var iteration=Stopwatch.StartNew();
    var snapshot=await probe.CaptureAsync();
    if(curveId is string activeId && File.Exists(Path.Combine(Curves.DirectoryFor(activeId),"result.json")))
        throw new IOException("Curve guardian ended early; do not label the remaining samples with its target");
    var current=backend.Capture();if(!WindowsCpuPolicyBackend.SamePolicy(active,current))throw new InvalidOperationException("External policy change; experiment stopped");
    if(clock.Elapsed.TotalSeconds>=nextModeCheck)
    {
     if(new WindowsPerformanceBackend().CaptureCurrent().RawShiftValue!=193)throw new InvalidOperationException("External hardware performance mode change; experiment stopped");
     nextModeCheck=clock.Elapsed.TotalSeconds+10;
    }
    if(snapshot.IsAcOnline!=true||snapshot.CpuTemperatureCelsius is null||snapshot.CpuTemperatureCelsius>=80||
       !new ThermalReading(snapshot.CpuTemperatureCelsius,snapshot.GpuTemperatureCelsius,snapshot.GpuPower).GpuWithin(74,false,DateTimeOffset.UtcNow))throw new InvalidOperationException("Temperature, GPU power evidence or AC source no longer qualifies");
    object? counters=null;string? counterError=null;
    try{
     using var search=new ManagementObjectSearcher("root\\cimv2","SELECT Name,ProcessorFrequency,PercentProcessorPerformance,PercentPerformanceLimit FROM Win32_PerfFormattedData_Counters_ProcessorInformation");search.Options.Timeout=TimeSpan.FromSeconds(3);
     using var rows=search.Get();var values=new List<object>();foreach(ManagementObject row in rows)using(row)values.Add(new{Name=row["Name"],ProcessorFrequency=row["ProcessorFrequency"],PercentProcessorPerformance=row["PercentProcessorPerformance"],PercentPerformanceLimit=row["PercentPerformanceLimit"]});counters=values;
    }catch(Exception ex){counterError=ex.Message;}
    if(mode=="video")
    {
     using var media=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"video-progress.json")));videoProof=media.RootElement.Clone();
     if(File.Exists(Path.Combine(folder,"video-error.json")))throw new IOException("Media playback reported an error");
    }
    log.WriteLine(JsonSerializer.Serialize(new{Scenario=scenario,Curve=curveName,Telemetry=snapshot,CpuPolicy=current,ProcessorCounters=counters,CounterError=counterError,Video=videoProof}));samples++;
    if(mode!="video"&&clock.Elapsed.TotalSeconds>=nextBurst)
    {
     var elapsed=OfficeBatch(payload);timings.Add(elapsed);nextBurst+=15;
     Recovery.Write(Path.Combine(folder,"latencies.json"),new{Workload="Fixed 20000-row JSON filter/sort, serialize, GZip roundtrip; one batch every 15 seconds",Milliseconds=timings});
     if(elapsed>2000)throw new InvalidOperationException("Representative task exceeded its two-second budget; no repeated load");
    }
    Recovery.Write(Path.Combine(folder,"progress.json"),new{Scenario=scenario,Seconds=clock.Elapsed.TotalSeconds,Samples=samples,Batches=timings.Count});
    await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1,2000-iteration.Elapsed.TotalMilliseconds)));
   }
   if(mode=="video"&&(videoProof is null||videoProof.Value.GetProperty("PlayedSeconds").GetDouble()-videoStartPosition<100))
    throw new IOException("Media playback progress did not keep up with the measured interval");
  }
 }
 catch(Exception ex){failure=ex.ToString();}
 finally
 {
  if(mode=="video")Recovery.Write(Path.Combine(folder,"video-stop"),new{Stop=true});
  try{restored=Recovery.Restore(journal);if(restored)Recovery.Write(Path.Combine(folder,"complete.json"),new{Verified=true,At=DateTimeOffset.UtcNow});}
  catch(Exception ex){failure=(failure??"")+" Recovery: "+ex;}
  pulse.Change(Timeout.Infinite,Timeout.Infinite);
  curveWorker?.Dispose();
 }
 try{await watcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));}catch(TimeoutException){failure=(failure??"")+" Independent recovery still owns its bounded deadline; it was not terminated.";}
 using var token=WindowsIdentity.GetCurrent();
 var passed=applied&&restored&&failure is null;
 Recovery.Write(Path.Combine(folder,"result.json"),new{Passed=passed,Applied=applied,Restored=restored,IsSimulated=false,Scenario=scenario,Curve=curveName,CurveRequestId=curveId,Before=before,Target=target,Samples=samples,TaskMilliseconds=timings,VideoProof=videoProof,VideoFramesMeasured=false,IsElevated=new WindowsPrincipal(token).IsInRole(WindowsBuiltInRole.Administrator),Error=failure,CompletedUtc=DateTimeOffset.UtcNow});
 return passed?0:2;
}
catch(Exception ex){Recovery.Write(Path.Combine(folder,"error.json"),new{Error=ex.ToString(),At=DateTimeOffset.UtcNow});return 2;}

static double OfficeBatch(byte[] payload)
{
 var timer=Stopwatch.StartNew();
 using var document=JsonDocument.Parse(payload);
 var selected=document.RootElement.EnumerateArray().Where(x=>x.GetProperty("Id").GetInt32()%3!=0).OrderByDescending(x=>x.GetProperty("Id").GetInt32()).ToArray();
 var normalized=JsonSerializer.SerializeToUtf8Bytes(selected);
 using var compressed=new MemoryStream();using(var zip=new GZipStream(compressed,CompressionLevel.Fastest,true))zip.Write(normalized);
 compressed.Position=0;using var unzip=new GZipStream(compressed,CompressionMode.Decompress);using var restored=new MemoryStream();unzip.CopyTo(restored);
 if(!SHA256.HashData(normalized).SequenceEqual(SHA256.HashData(restored.ToArray())))throw new InvalidDataException("Document roundtrip mismatch");
 return timer.Elapsed.TotalMilliseconds;
}
