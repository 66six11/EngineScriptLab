using ScriptLab;
using System.Text.Json;

namespace ScriptLab.Tests;

public sealed class ScriptLabJsonRpcServerTests
{
    [Fact]
    public void HandleRequest_WhenRunDebugIsCalled_ReturnsStoppedPausedAndTraceState()
    {
        var server = CreateServer();

        using var response = Send(
            server,
            1,
            "runDebug",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                graphNodeId = "n3",
                observeTrace = true
            });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal("com.game.PlayerMove", result.GetProperty("behaviorId").GetString());
        Assert.Equal("n3", result.GetProperty("stoppedEvent").GetProperty("graphNodeId").GetString());
        Assert.Equal("resolved", result.GetProperty("stoppedEvent").GetProperty("status").GetString());
        Assert.Equal("partial", result.GetProperty("pausedSnapshot").GetProperty("status").GetString());
        Assert.Contains(
            result.GetProperty("ingest").GetProperty("traceSnapshot").GetProperty("sites").EnumerateArray(),
            site => site.GetProperty("graphNodeId").GetString() == "n3");
    }

    [Fact]
    public void HandleRequest_WhenStatefulDebugFlowRuns_ExposesPausedSnapshotAndTraceSnapshot()
    {
        var server = CreateServer();
        using var loadGraph = Send(
            server,
            1,
            "loadGraph",
            new { scriptPath = GetSamplePath("PlayerMove.ash.cs") });
        var branchNodeId = loadGraph.RootElement
            .GetProperty("result")
            .GetProperty("graph")
            .GetProperty("functions")
            .EnumerateArray()
            .Single()
            .GetProperty("nodes")
            .EnumerateArray()
            .Single(node => node.GetProperty("kind").GetString() == "Branch")
            .GetProperty("id")
            .GetString();

        using var setBreakpoints = Send(
            server,
            2,
            "setBlueprintBreakpoints",
            new { graphNodeIds = new[] { branchNodeId } });
        var breakpoint = Assert.Single(setBreakpoints.RootElement
            .GetProperty("result")
            .GetProperty("breakpoints")
            .EnumerateArray());
        Assert.Equal(branchNodeId, breakpoint.GetProperty("graphNodeId").GetString());

        using var run = Send(server, 3, "runDebug", new { observeTrace = true });
        Assert.Equal(branchNodeId, run.RootElement
            .GetProperty("result")
            .GetProperty("stoppedEvent")
            .GetProperty("graphNodeId")
            .GetString());

        using var paused = Send(server, 4, "getPausedSnapshot", new { });
        var inspector = paused.RootElement
            .GetProperty("result")
            .GetProperty("scopes")
            .EnumerateArray()
            .Single(scope => scope.GetProperty("kind").GetString() == ScriptDebugScopeKind.Inspector);
        var speed = Assert.Single(
            inspector.GetProperty("variables").EnumerateArray(),
            variable => variable.GetProperty("name").GetString() == "Speed");
        Assert.Equal("4", speed.GetProperty("displayValue").GetString());

        using var trace = Send(server, 5, "getTraceSnapshot", new { });
        Assert.Contains(
            trace.RootElement.GetProperty("result").GetProperty("sites").EnumerateArray(),
            site => site.GetProperty("graphNodeId").GetString() == branchNodeId);
    }

    [Fact]
    public void HandleRequest_WhenResolvingSourceBreakpoint_ReturnsBlueprintBinding()
    {
        var server = CreateServer();

        using var response = Send(
            server,
            1,
            "resolveSourceBreakpoint",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                line = 13,
                column = 9
            });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal(ScriptBreakpointBindingStatus.Verified, result.GetProperty("status").GetString());
        Assert.Equal("Branch", result.GetProperty("candidates")[0].GetProperty("kind").GetString());
        Assert.Contains(
            "Opening brace",
            result.GetProperty("reason").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void HandleRequest_WhenSourceBreakpointsAreSet_RunDebugUsesThem()
    {
        var server = CreateServer();

        using var setSourceBreakpoints = Send(
            server,
            1,
            "setSourceBreakpoints",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                breakpoints = new[] { new { line = 12, column = 9 } }
            });
        var breakpoint = Assert.Single(setSourceBreakpoints.RootElement
            .GetProperty("result")
            .GetProperty("breakpoints")
            .EnumerateArray());
        Assert.Equal(ScriptBreakpointBindingStatus.Verified, breakpoint.GetProperty("status").GetString());
        Assert.True(breakpoint.GetProperty("hasSourceOrigin").GetBoolean());
        Assert.False(breakpoint.GetProperty("hasBlueprintOrigin").GetBoolean());

        var backend = Assert.Single(setSourceBreakpoints.RootElement
            .GetProperty("result")
            .GetProperty("backendResults")
            .EnumerateArray());
        Assert.Equal(ScriptBreakpointBackendStatus.Applied, backend.GetProperty("status").GetString());
        Assert.True(backend.GetProperty("verified").GetBoolean());

        using var run = Send(server, 2, "runDebug", new { });
        var stopped = run.RootElement.GetProperty("result").GetProperty("stoppedEvent");
        Assert.Equal(ScriptStoppedEventStatus.Resolved, stopped.GetProperty("status").GetString());
        Assert.Equal("Branch", stopped.GetProperty("binding").GetProperty("candidates")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public void HandleRequest_WhenGetBreakpointsIsCalled_ReturnsSharedBreakpointState()
    {
        var server = CreateServer();

        Send(
            server,
            1,
            "setSourceBreakpoints",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                breakpoints = new[] { new { line = 12, column = 9 } }
            }).Dispose();

        using var response = Send(server, 2, "getBreakpoints", new { });
        var breakpoint = Assert.Single(response.RootElement
            .GetProperty("result")
            .GetProperty("breakpoints")
            .EnumerateArray());
        Assert.Equal(ScriptBreakpointBindingStatus.Verified, breakpoint.GetProperty("status").GetString());
        Assert.True(breakpoint.GetProperty("hasSourceOrigin").GetBoolean());
        Assert.False(breakpoint.GetProperty("hasBlueprintOrigin").GetBoolean());
        Assert.Equal("Branch", breakpoint.GetProperty("binding").GetProperty("candidates")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public void HandleRequest_WhenRunDebugHasSourceBreakpoints_AppliesThemForThatRun()
    {
        var server = CreateServer();

        using var response = Send(
            server,
            1,
            "runDebug",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                sourceBreakpoints = new[] { new { line = 12, column = 9 } }
            });
        var result = response.RootElement.GetProperty("result");
        var breakpoint = Assert.Single(result.GetProperty("breakpoints").EnumerateArray());
        var backend = Assert.Single(result.GetProperty("backendResults").EnumerateArray());

        Assert.Equal(ScriptBreakpointBindingStatus.Verified, breakpoint.GetProperty("status").GetString());
        Assert.True(breakpoint.GetProperty("hasSourceOrigin").GetBoolean());
        Assert.Equal(ScriptBreakpointBackendStatus.Applied, backend.GetProperty("status").GetString());
        Assert.Equal("Branch", result
            .GetProperty("stoppedEvent")
            .GetProperty("binding")
            .GetProperty("candidates")[0]
            .GetProperty("kind")
            .GetString());
    }

    [Fact]
    public void HandleRequest_WhenMethodIsUnknown_ReturnsJsonRpcError()
    {
        var server = CreateServer();

        using var response = Send(server, 1, "missingMethod", new { });

        Assert.Equal(-32000, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains(
            "Unknown method",
            response.RootElement.GetProperty("error").GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    private static ScriptLabJsonRpcServer CreateServer()
    {
        return new ScriptLabJsonRpcServer(new ScriptLabServerOptions(CreateOutputDirectory()));
    }

    private static JsonDocument Send(
        ScriptLabJsonRpcServer server,
        int id,
        string method,
        object parameters)
    {
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        });
        return JsonDocument.Parse(server.HandleRequest(request));
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
