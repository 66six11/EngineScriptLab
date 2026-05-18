using ScriptLab;

namespace ScriptLab.Tests;

public sealed class ScriptDebugLoopTests
{
    [Fact]
    public void RunFile_WhenPlayerMoveRuns_ReturnsUnifiedDebugSnapshot()
    {
        var result = ScriptDebugLoop.RunFile(
            GetSamplePath("PlayerMove.ash.cs"),
            CreateOutputDirectory());
        var branchSite = FindSite(result, "Branch", "Branch");
        var translateSite = FindSite(result, "Call", "asharia.transform.translate");

        Assert.Equal("com.game.PlayerMove", result.BehaviorId);
        Assert.Equal(1, result.EntityId);
        Assert.Equal(branchSite.GraphNodeId, result.TargetGraphNodeId);
        Assert.Equal(branchSite.DebugSiteId, result.TargetDebugSiteId);

        var breakpoint = Assert.Single(result.Breakpoints, candidate => candidate.HasBlueprintOrigin);
        Assert.Equal(ScriptBreakpointBindingStatus.Verified, breakpoint.Status);
        Assert.Equal(branchSite.DebugSiteId, breakpoint.DebugSiteId);

        var backend = Assert.Single(result.BackendResults);
        Assert.Equal(ScriptBreakpointBackendStatus.Applied, backend.Status);
        Assert.True(backend.Verified);
        Assert.Equal(branchSite.ProbeId, backend.ProbeId);

        Assert.Contains(result.ProbeEvents, probeEvent =>
            probeEvent.Kind == "Enter" &&
            probeEvent.ProbeId == branchSite.ProbeId);
        Assert.Contains(result.ProbeEvents, probeEvent =>
            probeEvent.Kind == "Breakpoint" &&
            probeEvent.ProbeId == branchSite.ProbeId);
        Assert.Contains(result.ProbeEvents, probeEvent =>
            probeEvent.Kind == "Enter" &&
            probeEvent.ProbeId == translateSite.ProbeId);

        var stopped = Assert.Single(result.Ingest.StoppedEvents);
        Assert.Same(result.StoppedEvent, stopped);
        Assert.Equal(ScriptStoppedEventStatus.Resolved, stopped.Status);
        Assert.Equal(branchSite.DebugSiteId, stopped.DebugSiteId);
        Assert.True(stopped.Synthetic);

        Assert.NotNull(result.PausedSnapshot);
        var inspector = Assert.Single(result.PausedSnapshot.Scopes, scope => scope.Kind == ScriptDebugScopeKind.Inspector);
        var speed = Assert.Single(inspector.Variables, variable => variable.FieldId == "Speed");
        Assert.Equal("4", speed.DisplayValue);
        Assert.Equal(4.0f, speed.RawValue);

        Assert.Contains(result.Ingest.TraceSnapshot.Sites, site =>
            site.DebugSiteId == branchSite.DebugSiteId &&
            site.HitCount == 1);
        Assert.Contains(result.Ingest.TraceSnapshot.Sites, site =>
            site.DebugSiteId == translateSite.DebugSiteId &&
            site.HitCount == 1);
    }

    [Fact]
    public void RunFile_WhenGraphNodeIsProvided_UsesRequestedBreakpoint()
    {
        var first = ScriptDebugLoop.RunFile(
            GetSamplePath("PlayerMove.ash.cs"),
            CreateOutputDirectory());
        var translateSite = FindSite(first, "Call", "asharia.transform.translate");

        var result = ScriptDebugLoop.RunFile(
            GetSamplePath("PlayerMove.ash.cs"),
            CreateOutputDirectory(),
            new ScriptDebugLoopOptions(GraphNodeId: translateSite.GraphNodeId));

        Assert.Equal(translateSite.GraphNodeId, result.TargetGraphNodeId);
        Assert.Equal(translateSite.DebugSiteId, result.TargetDebugSiteId);
        var stopped = Assert.Single(result.Ingest.StoppedEvents);
        Assert.Equal(translateSite.DebugSiteId, stopped.DebugSiteId);
        Assert.Contains(result.Ingest.TraceSnapshot.Sites, site =>
            site.DebugSiteId == translateSite.DebugSiteId &&
            site.HitCount == 1);
    }

    private static ScriptDebugMapSite FindSite(ScriptDebugLoopResult result, string kind, string label)
    {
        var debugMap = System.Text.Json.JsonSerializer.Deserialize<ScriptDebugMap>(
            File.ReadAllText(result.DebugMapPath),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;

        return debugMap.Functions
            .SelectMany(function => function.Sites)
            .Single(site => site.Kind == kind && site.Label == label);
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            Guid.NewGuid().ToString("N"));
    }

    private static string GetSamplePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Samples", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate sample script '{fileName}'.");
    }
}
