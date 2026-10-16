using System.Text.Json;
using Gs66Control.Core;
using Gs66Control.Helper;
using Gs66Control.Windows;
using Xunit;

namespace Gs66Control.Tests;

public sealed class PerformanceActionTests
{
    [Theory]
    [InlineData(0xC0)]
    [InlineData(0xC1)]
    [InlineData(0xC2)]
    [InlineData(0xC4)]
    public void RequestAcceptsOnlyReviewedRawValues(byte raw)
    {
        var request = new PerformanceActionRequest(Guid.NewGuid(), raw, 0);
        request.Validate();
        Assert.True(PerformanceActionRequest.IsReviewedByte(raw));
    }

    [Theory]
    [InlineData(0xC3)]
    [InlineData(0x8D)]
    [InlineData(0x00)]
    public void RequestRejectsUnreviewedRawValues(byte raw)
    {
        var request = new PerformanceActionRequest(Guid.NewGuid(), raw, 0);
        Assert.Throws<InvalidDataException>(request.Validate);
    }

    [Fact]
    public async Task SimulatedFiveSecondTrialRestoresOriginalAndMarksProvenance()
    {
        using var workspace = new TemporaryWorkspace();
        var request = new PerformanceActionRequest(Guid.NewGuid(), 0xC4, 5);
        var directory = workspace.CreateRequest(request);
        var backend = new SimulatedPerformanceBackend(0xC1);
        var journalStates = new List<string>();
        backend.BeforeApply = () => journalStates.Add(ReadJournalState(directory));

        var result = await PerformanceActionExecutor.RunSimulationAsync(
            directory, backend, static (_, _) => Task.CompletedTask);

        Assert.True(result.IsSimulated);
        Assert.Equal("Restored", result.Outcome);
        Assert.Equal((byte)0xC1, result.OriginalRawByte);
        Assert.Equal((byte)0xC1, result.FinalRawByte);
        Assert.True(result.ReadBackVerified);
        Assert.True(result.RecoveryVerified);
        Assert.Equal((byte)0xC1, backend.Raw);
        Assert.Equal(2, backend.Writes);
        Assert.Equal(["Prepared", "RestoringPrepared"], journalStates);
        Assert.True(File.Exists(Path.Combine(directory, "journal.json")));
        Assert.True(File.Exists(Path.Combine(directory, "result.json")));
    }

    [Fact]
    public async Task FailedInitialAcknowledgementStillRestoresThroughPreparedJournal()
    {
        using var workspace = new TemporaryWorkspace();
        var request = new PerformanceActionRequest(Guid.NewGuid(), 0xC4, 5);
        var directory = workspace.CreateRequest(request);
        var backend = new SimulatedPerformanceBackend(0xC1) { FailNextApplyAfterWrite = true };
        var journalStates = new List<string>();
        backend.BeforeApply = () => journalStates.Add(ReadJournalState(directory));

        var result = await PerformanceActionExecutor.RunSimulationAsync(
            directory, backend, static (_, _) => Task.CompletedTask);

        Assert.Equal("Recovered", result.Outcome);
        Assert.False(result.ReadBackVerified);
        Assert.True(result.RecoveryVerified);
        Assert.Equal((byte)0xC1, result.FinalRawByte);
        Assert.Equal((byte)0xC1, backend.Raw);
        Assert.Equal(2, backend.Writes);
        Assert.Equal(["Prepared", "RestoringPrepared"], journalStates);
        Assert.Equal("Recovered", ReadJournalState(directory));
    }

    [Fact]
    public async Task CancellationDuringTrialRestoresThroughPreparedJournal()
    {
        using var workspace = new TemporaryWorkspace();
        var request = new PerformanceActionRequest(Guid.NewGuid(), 0xC4, 5);
        var directory = workspace.CreateRequest(request);
        var backend = new SimulatedPerformanceBackend(0xC1);
        using var cancellation = new CancellationTokenSource();
        var journalStates = new List<string>();
        backend.BeforeApply = () => journalStates.Add(ReadJournalState(directory));

        var result = await PerformanceActionExecutor.RunSimulationAsync(
            directory,
            backend,
            (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.Equal("Recovered", result.Outcome);
        Assert.True(result.RecoveryVerified);
        Assert.Equal((byte)0xC1, result.FinalRawByte);
        Assert.Equal((byte)0xC1, backend.Raw);
        Assert.Equal(2, backend.Writes);
        Assert.Equal(["Prepared", "RestoringPrepared"], journalStates);
    }

    [Fact]
    public async Task TrialDoesNotOverwriteAnExternalChange()
    {
        using var workspace = new TemporaryWorkspace();
        var request = new PerformanceActionRequest(Guid.NewGuid(), 0xC4, 5);
        var directory = workspace.CreateRequest(request);
        var backend = new SimulatedPerformanceBackend(0xC1);
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var resultTask = PerformanceActionExecutor.RunSimulationAsync(
            directory,
            backend,
            async (_, token) =>
            {
                backend.Raw = 0xC2;
                delayStarted.SetResult();
                await Task.Yield();
                token.ThrowIfCancellationRequested();
            });
        await delayStarted.Task;
        var result = await resultTask;

        Assert.True(result.IsSimulated);
        Assert.Equal("RecoveryFailed", result.Outcome);
        Assert.False(result.RecoveryVerified);
        Assert.Equal((byte)0xC2, result.FinalRawByte);
        Assert.Equal((byte)0xC2, backend.Raw);
        Assert.Equal(1, backend.Writes);
    }

    private static string ReadJournalState(string directory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "journal.json")));
        return document.RootElement.GetProperty("State").GetString()!;
    }

    [Fact]
    public async Task NormalActionReportsReadBackWithoutPretendingToRecover()
    {
        using var workspace = new TemporaryWorkspace();
        var request = new PerformanceActionRequest(Guid.NewGuid(), 0xC2, 0);
        var directory = workspace.CreateRequest(request);
        var backend = new SimulatedPerformanceBackend(0xC1);

        var result = await PerformanceActionExecutor.RunSimulationAsync(directory, backend);

        Assert.True(result.IsSimulated);
        Assert.Equal("Applied", result.Outcome);
        Assert.True(result.ReadBackVerified);
        Assert.False(result.RecoveryVerified);
        Assert.Equal((byte)0xC2, result.FinalRawByte);
        Assert.Equal(1, backend.Writes);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "gs66-performance-" + Guid.NewGuid().ToString("N"));
        public TemporaryWorkspace() => Directory.CreateDirectory(path);
        public string CreateRequest(PerformanceActionRequest request)
        {
            var directory = Path.Combine(path, request.RequestId.ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "request.json"), JsonSerializer.Serialize(request));
            return directory;
        }
        public void Dispose()
        {
            try { Directory.Delete(path, true); }
            catch (IOException) { }
        }
    }

    private sealed class SimulatedPerformanceBackend(byte initialRaw) : IPerformanceActionBackend
    {
        public bool IsSimulated => true;
        public byte Raw { get; set; } = initialRaw;
        public int Writes { get; private set; }
        public bool FailNextApplyAfterWrite { get; set; }
        public Action? BeforeApply { get; set; }

        public WindowsPerformanceSnapshot CaptureCurrent(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, DateTimeOffset.UtcNow,
                "Micro-Star International Co., Ltd.",
                "GS66 Stealth 10SE",
                "E16V1IMS.10C",
                "16V1EMS1.109",
                "0311202011:16:49",
                true, true, 7, Raw);
        }

        public WindowsPerformancePreview Preview(byte rawRequestedByte, CancellationToken cancellationToken = default)
        {
            var snapshot = CaptureCurrent(cancellationToken);
            var target = new WindowsPerformanceTarget(
                Gs66PerformanceMode.Customize,
                (Gs66ReviewedShiftValue)rawRequestedByte,
                false,
                "simulation");
            return new(snapshot, target, snapshot.RawShiftValue, false, []);
        }

        public WindowsPerformancePreview PreviewRestore(WindowsPerformanceSnapshot original, byte expectedCurrentRawByte) =>
            new(original,
                new WindowsPerformanceTarget(
                    Gs66PerformanceMode.Customize,
                    (Gs66ReviewedShiftValue)original.RawShiftValue,
                    false,
                    "simulation restore"),
                expectedCurrentRawByte, true, []);

        public WindowsPerformanceApplyResult Apply(WindowsPerformancePreview preview, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeApply?.Invoke();
            if (Raw != preview.ExpectedCurrentRawValue)
                throw new InvalidOperationException("simulated compare failed");
            var before = CaptureCurrent(cancellationToken);
            var target = (byte)preview.Target!.RawShift;
            var write = Raw != target;
            if (write)
            {
                Raw = target;
                Writes++;
                if (FailNextApplyAfterWrite)
                {
                    FailNextApplyAfterWrite = false;
                    throw new IOException("simulated lost write acknowledgement");
                }
            }
            var after = CaptureCurrent(cancellationToken);
            return new(preview, before, after, write);
        }
    }
}
