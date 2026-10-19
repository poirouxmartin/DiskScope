using DiskScope.Core;
using DiskScope.Scanner;

namespace DiskScope.Tests;

public sealed class ScanOrchestratorTests
{
    [Fact]
    public void SelectEngine_SkipsUnavailableAndPicksFirstAvailable()
    {
        var first = new FakeEngine("first", available: false, reason: "no driver");
        var second = new FakeEngine("second", available: true, reason: "ok");
        var third = new FakeEngine("third", available: true, reason: "ok");
        var orchestrator = new ScanOrchestrator(new IScanEngine[] { first, second, third });

        IScanEngine selected = orchestrator.SelectEngine(new ScanRequest("C:\\"));

        Assert.Same(second, selected);
    }

    [Fact]
    public void SelectEngine_PrefersEarlierAvailableEngine()
    {
        var first = new FakeEngine("first", available: true, reason: "ok");
        var second = new FakeEngine("second", available: true, reason: "ok");
        var orchestrator = new ScanOrchestrator(new IScanEngine[] { first, second });

        IScanEngine selected = orchestrator.SelectEngine(new ScanRequest("C:\\"));

        Assert.Same(first, selected);
    }

    [Fact]
    public void SelectEngine_ThrowsWithEveryReasonWhenNoneAvailable()
    {
        var first = new FakeEngine("first", available: false, reason: "no driver");
        var second = new FakeEngine("second", available: false, reason: "not NTFS");
        var orchestrator = new ScanOrchestrator(new IScanEngine[] { first, second });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
        {
            orchestrator.SelectEngine(new ScanRequest("C:\\"));
        });

        Assert.Contains("no driver", ex.Message);
        Assert.Contains("not NTFS", ex.Message);
    }

    [Fact]
    public async Task ScanAsync_RunsSelectedEngine()
    {
        var first = new FakeEngine("first", available: false, reason: "no driver");
        var second = new FakeEngine("second", available: true, reason: "ok");
        var orchestrator = new ScanOrchestrator(new IScanEngine[] { first, second });

        ScanResult result = await orchestrator.ScanAsync(new ScanRequest("C:\\"), null, CancellationToken.None);

        Assert.Equal("second", result.EngineName);
        Assert.Equal(ScanOutcome.Completed, result.Outcome);
    }

    private sealed class FakeEngine(string name, bool available, string reason) : IScanEngine
    {
        public string Name { get; } = name;

        public EngineAvailability CheckAvailability(ScanRequest request) => new(available, reason);

        public Task<ScanResult> ScanAsync(ScanRequest request, IScanObserver? observer, CancellationToken cancellationToken)
        {
            var builder = new NodeIndexBuilder();
            builder.AddRoot(request.RootPath);
            return Task.FromResult(new ScanResult(builder.Build(), ScanOutcome.Completed, TimeSpan.Zero, Name));
        }
    }
}
