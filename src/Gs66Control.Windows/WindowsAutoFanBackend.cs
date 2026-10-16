using System.Management;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using Gs66Control.Core;

namespace Gs66Control.Windows;

// Deliberately separate from the diagnostic/telemetry providers.
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoFanBackend : IAutoFanBackend
{
    // The approved single trial is complete. Normal builds keep that trial locked;
    // the separate automatic-only backend cannot restore its former manual baseline.
    public static bool FirstHardwareWriteApproved { get; } = false;
    private readonly bool automaticOnly;
    public WindowsAutoFanBackend() { }
    private WindowsAutoFanBackend(bool automaticOnly) => this.automaticOnly = automaticOnly;
    public static WindowsAutoFanBackend ForVerifiedAutomaticMode() => new(true);
    public bool IsSimulated => false;

    public AutoFanState Capture() => Capture(allowNvidiaFallback: true);

    public AutoFanState Capture(bool allowNvidiaFallback)
    {
        var started = DateTimeOffset.UtcNow;
        var (manufacturer, model) = ComputerIdentity();
        var bios = SingleValue("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion");
        RequireSchema("MSI_Software", "Software");
        RequireSchema("MSI_System", "System");
        RequireSchema("MSI_CPU", "CPU");
        RequireSchema("MSI_VGA", "VGA");
        var software = MsiWmiProperties.ReadIndexedBytes("MSI_Software", "Software", 34, default);
        var firmware = MsiFirmwareParser.ParseSoftwareBytes(Enumerable.Range(0, 34).Select(i => software[i]).ToArray())
            ?? throw new InvalidDataException("MSI firmware bytes could not be decoded.");
        var system = MsiWmiProperties.ReadIndexedBytes("MSI_System", "System", 21, default);
        var cpu = MsiWmiProperties.ReadIndexedBytes("MSI_CPU", "CPU", 19, default);
        var gpu = MsiWmiProperties.ReadIndexedBytes("MSI_VGA", "VGA", 18, default);
        var artifact = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "MSI", "One Dragon Center", "Base Module", "API_NB_Base Module.dll");
        using var file = File.OpenRead(artifact);
        var matches = Convert.ToHexString(SHA256.HashData(file)).Equals(AutoFanPolicy.VendorArtifactSha256, StringComparison.OrdinalIgnoreCase);
        var gpuTemperature = MsiTemperature.ToCelsius(gpu[1]);
        var gpuSource = "MSI_VGA WMI";
        GpuPowerEvidence? gpuPower = null;
        if (manufacturer == "Micro-Star International Co., Ltd." && model == "GS66 Stealth 10SE" &&
            bios == "E16V1IMS.10C" && firmware.Version == "16V1EMS1.109" && matches)
        {
            try { gpuPower = new WindowsGpuPowerProbe().Capture(); }
            catch (Exception ex) when (ex is ManagementException or IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            { if (gpuTemperature is null) gpuSource = "Unavailable; GPU device power state could not be verified"; }
        }
        if (gpuTemperature is null && gpuPower?.DevicePowerState == 4)
            gpuSource = "Temperature unavailable; Windows reports GPU D3";
        if (allowNvidiaFallback && gpuTemperature is null && gpuPower?.DevicePowerState == 1 &&
            manufacturer == "Micro-Star International Co., Ltd." && model == "GS66 Stealth 10SE" &&
            bios == "E16V1IMS.10C" && firmware.Version == "16V1EMS1.109" && matches)
        {
            gpuTemperature = ReadNvidiaTemperature();
            if (gpuTemperature is not null) gpuSource = "nvidia-smi read-only query; verified installed binary";
        }
        return new(false, started, manufacturer, model, bios, firmware.Version, firmware.BuildText,
            matches, true, system[9], MsiTemperature.ToCelsius(cpu[1]), gpuTemperature) { GpuTemperatureSource = gpuSource, GpuPower = gpuPower };
    }

    public void SetAutomatic(byte expectedMode) => Write(expectedMode, AutoFanPolicy.Automatic, false);

    public void RestoreTrialBaseline(byte expectedMode, byte baseline)
    {
        if (expectedMode != AutoFanPolicy.Automatic || baseline != AutoFanPolicy.TrialBaseline)
            throw new InvalidOperationException("Only the reviewed trial baseline can be restored.");
        Write(expectedMode, baseline, true);
    }

    private void Write(byte expected, byte target, bool restoring)
    {
        if (automaticOnly && target != AutoFanPolicy.Automatic) throw new InvalidOperationException("This helper can only apply manufacturer automatic mode.");
        if (!FirstHardwareWriteApproved && !automaticOnly) throw new InvalidOperationException("The single hardware trial is locked in this build.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("The one-time trial requires an explicitly elevated helper; no automatic elevation is attempted.");
        var state = Capture();
        AutoFanPolicy.Require(state, false, restoring || expected != AutoFanPolicy.Automatic);
        if (state.Mode != expected) throw new InvalidOperationException("Fan mode changed concurrently; update refused.");
        if (expected == target) return;
        if (target is not (AutoFanPolicy.Automatic or AutoFanPolicy.TrialBaseline))
            throw new InvalidOperationException("Unsupported fan mode.");
        using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSI_System");
        searcher.Options.Timeout = TimeSpan.FromSeconds(5);
        using var rows = searcher.Get();
        ManagementObject? selected = null;
        var objects = new List<ManagementObject>();
        try
        {
            foreach (ManagementObject row in rows) objects.Add(row);
            if (objects.Count != 21) throw new InvalidDataException("MSI_System instance count changed.");
            string? prefix = null;
            for (var index = 0; index < objects.Count; index++)
            {
                var row = objects[index];
                var name = row["InstanceName"] as string ?? "";
                var split = name.LastIndexOf('_');
                if (split < 0 || name[(split + 1)..] != index.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    row["Active"] is not true || row["System"] is not byte ||
                    (prefix is not null && prefix != name[..split]))
                    throw new InvalidDataException("MSI_System instance binding changed.");
                prefix = name[..split];
                if (index == 9) selected = row;
            }
            if (selected is null || (byte)selected["System"] != expected)
                throw new InvalidOperationException("Fan mode changed before Put; update refused.");
            selected["System"] = target;
            selected.Put(new PutOptions { Type = PutType.UpdateOnly, Timeout = TimeSpan.FromSeconds(5) });
        }
        finally { foreach (var row in objects) row.Dispose(); }
        if (MsiWmiProperties.ReadIndexedBytes("MSI_System", "System", 21, default)[9] != target)
            throw new IOException("WMI update read-back does not match; recovery must assess the current state.");
    }

    private static int? ReadNvidiaTemperature()
    {
        const string reviewedToolHash = "B7D26DAB016A9DC4C2DB725633C8EC1A7B58310B18A6610A6DEC7D67057DB248";
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
        if (!File.Exists(path)) return null;
        using (var file = File.OpenRead(path))
            if (Convert.ToHexString(SHA256.HashData(file)) != reviewedToolHash) return null;
        var start = new ProcessStartInfo(path)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--query-gpu=name,temperature.gpu");
        start.ArgumentList.Add("--format=csv,noheader,nounits");
        using var process = Process.Start(start) ?? throw new IOException("Could not start the reviewed NVIDIA read-only tool.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(2000)) { process.Kill(); process.WaitForExit(); return null; }
        errors.GetAwaiter().GetResult();
        return process.ExitCode == 0 ? NvidiaTemperature.ParseSingleGs66Gpu(output.GetAwaiter().GetResult()) : null;
    }

    private static (string Manufacturer, string Model) ComputerIdentity()
    {
        using var query = new ManagementObjectSearcher("root\\cimv2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
        query.Options.Timeout = TimeSpan.FromSeconds(5);
        using var results = query.Get();
        if (results.Count != 1) throw new InvalidDataException("Expected one computer identity.");
        foreach (ManagementBaseObject row in results)
            using (row) return (row["Manufacturer"]?.ToString() ?? "", row["Model"]?.ToString() ?? "");
        throw new InvalidDataException("Computer identity unavailable.");
    }

    private static void RequireSchema(string className, string valueProperty)
    {
        using var schema = new ManagementClass("root\\WMI", className, null);
        schema.Options.Timeout = TimeSpan.FromSeconds(5);
        schema.Get();
        var data = schema.Properties[valueProperty];
        var key = schema.Properties["InstanceName"];
        var active = schema.Properties["Active"];
        if (data.Type != CimType.UInt8 || data.IsArray || key.Type != CimType.String || key.IsArray ||
            key.Qualifiers["Key"].Value is not true || active.Type != CimType.Boolean || active.IsArray)
            throw new InvalidDataException($"{className} property/key schema changed.");
    }

    private static string SingleValue(string query, string property)
    {
        using var searcher = new ManagementObjectSearcher("root\\cimv2", query);
        searcher.Options.Timeout = TimeSpan.FromSeconds(5);
        using var results = searcher.Get();
        if (results.Count != 1) throw new InvalidDataException("Expected one BIOS identity.");
        foreach (ManagementBaseObject row in results) using (row) return row[property]?.ToString() ?? "";
        throw new InvalidDataException("BIOS identity unavailable.");
    }
}
