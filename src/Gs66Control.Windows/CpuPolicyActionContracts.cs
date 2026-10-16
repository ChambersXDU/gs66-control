namespace Gs66Control.Windows;

public sealed record CpuPolicyActionRequest(Guid RequestId, CpuPolicySnapshot Expected, CpuPolicyValues Target,
    bool Ac, CpuPolicySnapshot? RestorePrevious, DateTimeOffset RequestedAtUtc, int ParentPid, DateTimeOffset ParentStartUtc)
{
    public void Validate(DateTimeOffset now)
    {
        if (RequestId == Guid.Empty || Expected is null || Target is null || ParentPid <= 0 ||
            now - RequestedAtUtc > TimeSpan.FromMinutes(2) || RequestedAtUtc > now.AddSeconds(1) ||
            ParentStartUtc > RequestedAtUtc || Expected.IsSimulated || RestorePrevious?.IsSimulated == true)
            throw new InvalidDataException("CPU policy action identity, provenance or age is invalid.");
        Target.ValidateTarget();
    }
}

public sealed record CpuPolicyActionResult(Guid RequestId, bool Verified, CpuPolicySnapshot? Before,
    CpuPolicySnapshot? After, string? Error);

public sealed record CpuPolicyChange(CpuPolicySnapshot Before, CpuPolicySnapshot After, bool Ac, string? PresetId);
