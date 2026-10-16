using System.Diagnostics;
using System.Text.Json;
using System.Runtime.Versioning;
using Gs66Control.Windows;

namespace Gs66Control.Helper;

[SupportedOSPlatform("windows")]
internal static class CpuPolicyAction
{
    public static string DirectoryFor(Guid id) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GS66Control", "cpu-policy-requests", id.ToString("N"));

    public static int Run(Guid id)
    {
        var directory = DirectoryFor(id);
        CpuPolicySnapshot? before = null;
        CpuPolicyActionResult result;
        try
        {
            var path = Path.Combine(directory, "request.json");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 64 * 1024)
                throw new InvalidDataException("Invalid CPU policy request file.");
            var request = JsonSerializer.Deserialize<CpuPolicyActionRequest>(File.ReadAllText(path))
                ?? throw new InvalidDataException("CPU policy request is empty.");
            request.Validate(DateTimeOffset.UtcNow);
            if (request.RequestId != id) throw new InvalidDataException("CPU policy request id mismatch.");
            using var parent = Process.GetProcessById(request.ParentPid);
            if (parent.HasExited || new DateTimeOffset(parent.StartTime.ToUniversalTime()) != request.ParentStartUtc)
                throw new InvalidDataException("The requesting application is no longer running.");
            using var mutex = new Mutex(false, @"Global\Gs66Control.CpuPolicy");
            var acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Another CPU policy action is active.");
                var backend = new WindowsCpuPolicyBackend();
                before = backend.Capture();
                if (!WindowsCpuPolicyBackend.SamePolicy(request.Expected, before))
                    throw new IOException("CPU policy changed while requesting Windows permission; refresh and retry.");
                Publish(Path.Combine(directory, "journal.json"), new { RequestId = id, Stage = "Prepared", Before = before });
                var after = request.RestorePrevious is { } previous
                    ? backend.Restore(previous, before, request.Ac)
                    : backend.Apply(before, request.Target, request.Ac);
                var independent = new WindowsCpuPolicyBackend().Capture();
                if (!WindowsCpuPolicyBackend.SamePolicy(after, independent)) throw new IOException("Independent CPU policy read-back failed.");
                result = new(id, true, before, independent, null);
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { result = new(id, false, before, null, ex.Message); }
        Publish(Path.Combine(directory, "result.json"), result);
        return result.Verified ? 0 : 2;
    }

    private static void Publish(string path, object value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
