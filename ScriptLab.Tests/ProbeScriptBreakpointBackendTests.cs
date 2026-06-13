using ScriptLab;

namespace ScriptLab.Tests;

public sealed class ProbeScriptBreakpointBackendTests
{
    [Fact]
    public void ApplySourceBreakpoints_WhenBreakpointIsVerified_EnablesHostProbeBreakpoint()
    {
        var emit = EmitPlayerMove();
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");
        var host = DotnetDebugHost.Load(emit);
        var session = CreateSession(emit);
        var backend = new ProbeScriptBreakpointBackend(host);

        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(12, 9) });
        var results = session.ApplySourceBreakpoints(backend, emit.DebugMap.SourceDocumentPath);

        var result = Assert.Single(results);
        Assert.Equal(ScriptBreakpointBackendStatus.Applied, result.Status);
        Assert.False(result.Verified);
        Assert.True(result.Synthetic);
        Assert.Equal(branchProbe.ProbeId, result.ProbeId);
        Assert.Equal(new[] { branchProbe.ProbeId }, host.GetBreakpointProbeIds());

        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);

        var breakpointEvent = Assert.Single(host.GetProbeEvents(), probeEvent =>
            probeEvent.Kind == "Breakpoint" &&
            probeEvent.ProbeId == branchProbe.ProbeId);
        var stopped = session.ResolveStoppedEvent(breakpointEvent);

        Assert.Equal(ScriptStoppedEventStatus.Resolved, stopped.Status);
        Assert.Equal(ScriptStoppedReason.Breakpoint, stopped.Reason);
        Assert.True(stopped.Synthetic);
        Assert.False(stopped.AllThreadsStopped);
        Assert.Equal(branchProbe.ProbeId, stopped.ProbeId);
        Assert.Equal(branchProbe.DebugSiteId, stopped.DebugSiteId);
        Assert.Equal(branchProbe.GraphNodeId, stopped.GraphNodeId);
        Assert.Equal(emit.DebugMap.SourceDocumentPath, stopped.SourcePath);
        Assert.Equal(12, stopped.Line);
        Assert.Equal(9, stopped.Column);
    }

    [Fact]
    public void ApplySourceBreakpoints_WhenSourceSetIsReplacedWithEmpty_ClearsHostProbeBreakpoint()
    {
        var emit = EmitPlayerMove();
        var host = DotnetDebugHost.Load(emit);
        var session = CreateSession(emit);
        var backend = new ProbeScriptBreakpointBackend(host);

        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(12, 9) });
        session.ApplySourceBreakpoints(backend, emit.DebugMap.SourceDocumentPath);
        Assert.NotEmpty(host.GetBreakpointProbeIds());

        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            Array.Empty<ScriptSourceBreakpointRequest>());
        var results = session.ApplySourceBreakpoints(backend, emit.DebugMap.SourceDocumentPath);

        Assert.Empty(results);
        Assert.Empty(host.GetBreakpointProbeIds());
    }

    [Fact]
    public void ApplySourceBreakpoints_WhenBlueprintOriginRemains_KeepsBlueprintProbeBreakpoint()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.DebugSiteId == branchSite.DebugSiteId);
        var translateSite = FindSite(emit.DebugMap, "Call", "asharia.transform.translate");
        var translateProbe = Assert.Single(emit.ProbeSites, site => site.DebugSiteId == translateSite.DebugSiteId);
        var host = DotnetDebugHost.Load(emit);
        var session = CreateSession(emit);
        var backend = new ProbeScriptBreakpointBackend(host);

        session.SetBlueprintBreakpoints(new[] { branchSite.GraphNodeId });
        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(14, 13) });
        session.ApplySourceBreakpoints(backend, emit.DebugMap.SourceDocumentPath);

        Assert.Contains(branchProbe.ProbeId, host.GetBreakpointProbeIds());
        Assert.Contains(translateProbe.ProbeId, host.GetBreakpointProbeIds());

        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            Array.Empty<ScriptSourceBreakpointRequest>());
        var results = session.ApplySourceBreakpoints(backend, emit.DebugMap.SourceDocumentPath);

        var result = Assert.Single(results);
        Assert.Equal(branchSite.DebugSiteId, result.DebugSiteId);
        Assert.False(result.Verified);
        Assert.True(result.Synthetic);
        Assert.Equal(new[] { branchProbe.ProbeId }, host.GetBreakpointProbeIds());
    }

    [Fact]
    public void ApplySourceBreakpoints_WhenBreakpointIsSourceOnly_ReturnsUnsupported()
    {
        var emit = EmitPlayerMove();
        var host = DotnetDebugHost.Load(emit);
        var session = CreateSession(emit);
        var backend = new ProbeScriptBreakpointBackend(host);

        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(1, 1) });
        var results = session.ApplySourceBreakpoints(backend, emit.DebugMap.SourceDocumentPath);

        var result = Assert.Single(results);
        Assert.Equal(ScriptBreakpointBackendStatus.Unsupported, result.Status);
        Assert.False(result.Verified);
        Assert.True(result.Synthetic);
        Assert.Null(result.DebugSiteId);
        Assert.Empty(host.GetBreakpointProbeIds());
    }

    private static DebugSessionCore CreateSession(DebugScriptEmitResult emit)
    {
        return new DebugSessionCore(
            emit.DebugMap,
            File.ReadAllText(emit.DebugMap.SourceDocumentPath));
    }

    private static DebugScriptEmitResult EmitPlayerMove()
    {
        return SourceInstrumentedDebugCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            Path.Combine(
                Path.GetTempPath(),
                "ScriptLab.Tests",
                Guid.NewGuid().ToString("N")));
    }

    private static ScriptDebugMapSite FindSite(ScriptDebugMap debugMap, string kind, string label)
    {
        return debugMap.Functions
            .SelectMany(function => function.Sites)
            .Single(site => site.Kind == kind && site.Label == label);
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
