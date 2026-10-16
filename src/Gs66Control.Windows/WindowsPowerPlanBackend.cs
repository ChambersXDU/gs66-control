using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Gs66Control.Windows;

/// <summary>
/// The native boundary used by <see cref="WindowsPowerPlanBackend"/>. The
/// production implementation calls documented PowrProf APIs; tests can use a
/// deterministic adapter without invoking Windows.
/// </summary>
public interface IWindowsPowerPlanNativeAdapter
{
    bool IsSimulated { get; }

    IReadOnlyList<Guid> EnumerateSchemeGuids();

    Guid GetActiveScheme();

    string ReadFriendlyName(Guid schemeGuid);

    void SetActiveScheme(Guid schemeGuid);
}

/// <summary>A single existing Windows power scheme.</summary>
public sealed record WindowsPowerPlan(
    Guid SchemeGuid,
    string FriendlyName,
    bool IsSimulated);

/// <summary>A fresh list of schemes and active-scheme snapshot.</summary>
public sealed record WindowsPowerPlanSnapshot(
    IReadOnlyList<WindowsPowerPlan> Plans,
    Guid ActiveSchemeGuid,
    DateTimeOffset CapturedAtUtc,
    bool IsSimulated)
{
    public WindowsPowerPlan? ActivePlan =>
        Plans.FirstOrDefault(plan => plan.SchemeGuid == ActiveSchemeGuid);
}

/// <summary>The result of one compare-before-activate transaction.</summary>
public sealed record WindowsPowerPlanApplyResult(
    WindowsPowerPlanSnapshot Before,
    WindowsPowerPlanSnapshot After,
    bool PowerPlanChanged);

/// <summary>
/// Parses the byte layout returned by PowerEnumerate for a native GUID. The
/// layout is the Windows GUID memory layout, which is the layout consumed by
/// the span-based Guid constructor.
/// </summary>
public static class WindowsPowerPlanGuidParser
{
    public const int GuidByteLength = 16;

    public static Guid Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != GuidByteLength)
            throw new InvalidDataException(
                $"A power scheme GUID must contain exactly {GuidByteLength} bytes.");

        return new Guid(bytes);
    }
}

/// <summary>
/// Enumerates and switches existing Windows power schemes through documented
/// PowrProf APIs. It never creates, deletes, or edits a scheme setting.
/// </summary>
public sealed class WindowsPowerPlanBackend
{
    public const int MaximumExistingSchemes = 64;
    public const int MaximumFriendlyNameBytes = 4 * 1024;

    private readonly IWindowsPowerPlanNativeAdapter native;

    [SupportedOSPlatform("windows")]
    public WindowsPowerPlanBackend()
        : this(new PowrProfPowerPlanNativeAdapter())
    {
    }

    public WindowsPowerPlanBackend(IWindowsPowerPlanNativeAdapter nativeAdapter)
    {
        native = nativeAdapter ?? throw new ArgumentNullException(nameof(nativeAdapter));
    }

    /// <summary>
    /// Captures all existing schemes and the active scheme independently from
    /// any earlier snapshot. A missing or malformed scheme is a hard failure so
    /// callers cannot present incomplete power-plan choices as authoritative.
    /// </summary>
    public WindowsPowerPlanSnapshot Capture(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var schemeGuids = native.EnumerateSchemeGuids()
            ?? throw new InvalidDataException("PowerEnumerate returned no collection.");

        if (schemeGuids.Count == 0)
            throw new InvalidDataException("No existing Windows power schemes were enumerated.");
        if (schemeGuids.Count > MaximumExistingSchemes)
            throw new InvalidDataException(
                $"PowerEnumerate returned more than {MaximumExistingSchemes} existing schemes.");

        var plans = new List<WindowsPowerPlan>(schemeGuids.Count);
        var seen = new HashSet<Guid>();
        foreach (var schemeGuid in schemeGuids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (schemeGuid == Guid.Empty)
                throw new InvalidDataException("PowerEnumerate returned an empty scheme GUID.");
            if (!seen.Add(schemeGuid))
                throw new InvalidDataException($"PowerEnumerate returned duplicate scheme GUID {schemeGuid}.");

            var friendlyName = native.ReadFriendlyName(schemeGuid);
            ValidateFriendlyName(schemeGuid, friendlyName);
            plans.Add(new WindowsPowerPlan(schemeGuid, friendlyName, native.IsSimulated));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var activeSchemeGuid = native.GetActiveScheme();
        if (activeSchemeGuid == Guid.Empty)
            throw new InvalidDataException("PowerGetActiveScheme returned an empty scheme GUID.");
        if (!seen.Contains(activeSchemeGuid))
            throw new InvalidDataException(
                $"The active scheme {activeSchemeGuid} was not present in the enumerated schemes.");

        return new(
            Plans: plans.AsReadOnly(),
            ActiveSchemeGuid: activeSchemeGuid,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            IsSimulated: native.IsSimulated);
    }

    /// <summary>
    /// Activates an existing scheme only if the freshly captured active GUID
    /// still equals <paramref name="expectedCurrentScheme"/>. The final
    /// capture is independent and must observe the requested active GUID.
    /// </summary>
    public WindowsPowerPlanApplyResult Activate(
        Guid targetScheme,
        Guid expectedCurrentScheme,
        CancellationToken cancellationToken = default)
    {
        if (targetScheme == Guid.Empty)
            throw new ArgumentException("The target scheme GUID cannot be empty.", nameof(targetScheme));
        if (expectedCurrentScheme == Guid.Empty)
            throw new ArgumentException(
                "The expected current scheme GUID cannot be empty.", nameof(expectedCurrentScheme));

        var before = Capture(cancellationToken);
        if (before.ActiveSchemeGuid != expectedCurrentScheme)
            throw new InvalidOperationException(
                $"The active power scheme changed before activation. Expected {expectedCurrentScheme}, " +
                $"found {before.ActiveSchemeGuid}.");
        if (before.Plans.All(plan => plan.SchemeGuid != targetScheme))
            throw new InvalidOperationException(
                $"The target power scheme {targetScheme} was not in the fresh existing-scheme list.");

        cancellationToken.ThrowIfCancellationRequested();
        var changed = before.ActiveSchemeGuid != targetScheme;
        if (changed)
            native.SetActiveScheme(targetScheme);

        cancellationToken.ThrowIfCancellationRequested();
        var after = Capture(cancellationToken);
        if (after.ActiveSchemeGuid != targetScheme)
            throw new IOException(
                $"PowerSetActiveScheme read-back was {after.ActiveSchemeGuid}, expected {targetScheme}.");

        return new(before, after, changed);
    }

    private static void ValidateFriendlyName(Guid schemeGuid, string? friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName) || friendlyName.IndexOf('\0') >= 0)
            throw new InvalidDataException(
                $"PowerReadFriendlyName returned an invalid name for scheme {schemeGuid}.");
        if (Encoding.Unicode.GetByteCount(friendlyName) > MaximumFriendlyNameBytes - sizeof(char))
            throw new InvalidDataException(
                $"PowerReadFriendlyName returned an oversized name for scheme {schemeGuid}.");
    }
}

[SupportedOSPlatform("windows")]
internal sealed class PowrProfPowerPlanNativeAdapter : IWindowsPowerPlanNativeAdapter
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint ErrorNoMoreItems = 259;
    private const uint AccessScheme = 16;

    public bool IsSimulated => false;

    public IReadOnlyList<Guid> EnumerateSchemeGuids()
    {
        var schemes = new List<Guid>(WindowsPowerPlanBackend.MaximumExistingSchemes);
        for (uint index = 0; index < WindowsPowerPlanBackend.MaximumExistingSchemes; index++)
        {
            var result = ReadSchemeGuid(index, out var schemeGuid);
            if (result == ErrorNoMoreItems)
                return schemes.AsReadOnly();
            if (result != ErrorSuccess)
                ThrowNativeFailure("PowerEnumerate", result);

            schemes.Add(schemeGuid);
        }

        var overflowResult = ReadSchemeGuid(WindowsPowerPlanBackend.MaximumExistingSchemes, out _);
        if (overflowResult == ErrorNoMoreItems)
            return schemes.AsReadOnly();
        if (overflowResult == ErrorSuccess || overflowResult == ErrorMoreData)
            throw new InvalidDataException(
                $"PowerEnumerate returned more than {WindowsPowerPlanBackend.MaximumExistingSchemes} schemes.");
        ThrowNativeFailure("PowerEnumerate", overflowResult);
        return schemes.AsReadOnly();
    }

    public Guid GetActiveScheme()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var activeSchemePointer);
        if (result != ErrorSuccess)
            ThrowNativeFailure("PowerGetActiveScheme", result);
        if (activeSchemePointer == IntPtr.Zero)
            throw new InvalidDataException("PowerGetActiveScheme returned a null GUID pointer.");

        try
        {
            return Marshal.PtrToStructure<Guid>(activeSchemePointer);
        }
        finally
        {
            _ = LocalFree(activeSchemePointer);
        }
    }

    public string ReadFriendlyName(Guid schemeGuid)
    {
        uint bufferSize = 0;
        var result = PowerReadFriendlyName(
            IntPtr.Zero,
            ref schemeGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            null,
            ref bufferSize);
        if (result != ErrorSuccess)
            ThrowNativeFailure("PowerReadFriendlyName", result);
        if (bufferSize == 0 || bufferSize > WindowsPowerPlanBackend.MaximumFriendlyNameBytes ||
            bufferSize % sizeof(char) != 0)
            throw new InvalidDataException(
                $"PowerReadFriendlyName returned an invalid UTF-16 size ({bufferSize}) for {schemeGuid}.");

        var buffer = new byte[checked((int)bufferSize)];
        var returnedSize = bufferSize;
        result = PowerReadFriendlyName(
            IntPtr.Zero,
            ref schemeGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            buffer,
            ref returnedSize);
        if (result != ErrorSuccess)
            ThrowNativeFailure("PowerReadFriendlyName", result);
        if (returnedSize == 0 || returnedSize > buffer.Length || returnedSize % sizeof(char) != 0)
            throw new InvalidDataException(
                $"PowerReadFriendlyName returned an invalid UTF-16 result size ({returnedSize}) for {schemeGuid}.");

        try
        {
            var decoded = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true)
                .GetString(buffer, 0, checked((int)returnedSize));
            var terminator = decoded.IndexOf('\0');
            if (terminator <= 0 || decoded[(terminator + 1)..].Any(character => character != '\0'))
                throw new InvalidDataException(
                    $"PowerReadFriendlyName returned an unterminated or malformed name for {schemeGuid}.");
            return decoded[..terminator];
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                $"PowerReadFriendlyName returned invalid UTF-16 for {schemeGuid}.", exception);
        }
    }

    public void SetActiveScheme(Guid schemeGuid)
    {
        var result = PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
        if (result != ErrorSuccess)
            ThrowNativeFailure("PowerSetActiveScheme", result);
    }

    private static uint ReadSchemeGuid(uint index, out Guid schemeGuid)
    {
        schemeGuid = Guid.Empty;
        uint requiredBytes = 0;
        var result = PowerEnumerate(
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            AccessScheme,
            index,
            null,
            ref requiredBytes);
        if (result == ErrorNoMoreItems)
            return result;
        if (result != ErrorMoreData || requiredBytes != WindowsPowerPlanGuidParser.GuidByteLength)
            return result == ErrorSuccess
                ? ErrorMoreData
                : result;

        var bytes = new byte[WindowsPowerPlanGuidParser.GuidByteLength];
        var bufferSize = (uint)bytes.Length;
        result = PowerEnumerate(
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            AccessScheme,
            index,
            bytes,
            ref bufferSize);
        if (result != ErrorSuccess)
            return result;
        if (bufferSize != bytes.Length)
            throw new InvalidDataException(
                $"PowerEnumerate returned {bufferSize} bytes for a power scheme GUID.");

        schemeGuid = WindowsPowerPlanGuidParser.Parse(bytes);
        return ErrorSuccess;
    }

    private static void ThrowNativeFailure(string functionName, uint result) =>
        throw new InvalidOperationException($"{functionName} failed with Win32 error {result}.");

    [DllImport("PowrProf.dll", ExactSpelling = true, SetLastError = false)]
    private static extern uint PowerEnumerate(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subgroupGuid,
        uint accessFlags,
        uint index,
        [Out] byte[]? buffer,
        ref uint bufferSize);

    [DllImport("PowrProf.dll", ExactSpelling = true, SetLastError = false)]
    private static extern uint PowerGetActiveScheme(
        IntPtr userRootPowerKey,
        out IntPtr activePolicyGuid);

    [DllImport("PowrProf.dll", ExactSpelling = true, SetLastError = false)]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subgroupGuid,
        IntPtr powerSettingGuid,
        [Out] byte[]? buffer,
        ref uint bufferSize);

    [DllImport("PowrProf.dll", ExactSpelling = true, SetLastError = false)]
    private static extern uint PowerSetActiveScheme(
        IntPtr userRootPowerKey,
        ref Guid schemeGuid);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = false)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
