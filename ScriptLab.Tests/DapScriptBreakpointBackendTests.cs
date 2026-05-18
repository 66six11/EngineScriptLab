using ScriptLab;
using System.Text.Json.Nodes;

namespace ScriptLab.Tests;

public sealed class DapScriptBreakpointBackendTests
{
    [Fact]
    public void ReplaceSourceBreakpoints_WhenAdapterResponds_MapsBreakpointsByIndex()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var breakpoints = session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[]
            {
                new ScriptSourceBreakpointRequest(12, 9),
                new ScriptSourceBreakpointRequest(1, 1)
            })
            .OrderBy(breakpoint => breakpoint.Line == 12 ? 0 : 1)
            .ToArray();
        var client = new FakeDapRequestClient();
        client.EnqueueResponse(new JsonObject
        {
            ["breakpoints"] = new JsonArray
            {
                new JsonObject
                {
                    ["verified"] = true,
                    ["line"] = 12,
                    ["column"] = 9
                },
                new JsonObject
                {
                    ["verified"] = false,
                    ["message"] = "No executable code was found."
                }
            }
        });
        var backend = new DapScriptBreakpointBackend(client);

        var results = backend.ReplaceSourceBreakpoints(emit.DebugMap.SourceDocumentPath, breakpoints);

        var request = Assert.Single(client.Requests);
        Assert.Equal("setBreakpoints", request.Command);
        Assert.Equal(Path.GetFullPath(emit.DebugMap.SourceDocumentPath), request.Arguments["source"]!
            .AsObject()["path"]!
            .GetValue<string>());
        var requestedBreakpoints = request.Arguments["breakpoints"]!.AsArray();
        Assert.Equal(2, requestedBreakpoints.Count);
        Assert.Equal(12, requestedBreakpoints[0]!.AsObject()["line"]!.GetValue<int>());
        Assert.Equal(9, requestedBreakpoints[0]!.AsObject()["column"]!.GetValue<int>());
        Assert.Equal(1, requestedBreakpoints[1]!.AsObject()["line"]!.GetValue<int>());
        Assert.True(request.Arguments["sourceModified"]!.GetValue<bool>() is false);

        Assert.Equal(2, results.Count);
        Assert.Equal(ScriptBreakpointBackendStatus.Applied, results[0].Status);
        Assert.True(results[0].Verified);
        Assert.Equal(ScriptBreakpointBackendStatus.Unbound, results[1].Status);
        Assert.False(results[1].Verified);
        Assert.Contains("No executable code", results[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceSourceBreakpoints_WhenCapabilitiesAllowConditions_SendsConditionFields()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var breakpoints = session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(12, 9, "Speed > 0", "3") });
        var client = new FakeDapRequestClient();
        client.EnqueueResponse(new JsonObject
        {
            ["breakpoints"] = new JsonArray
            {
                new JsonObject
                {
                    ["verified"] = true
                }
            }
        });
        var backend = new DapScriptBreakpointBackend(
            client,
            new DapBreakpointBackendCapabilities(
                SupportsConditionalBreakpoints: true,
                SupportsHitConditionalBreakpoints: true));

        var results = backend.ReplaceSourceBreakpoints(emit.DebugMap.SourceDocumentPath, breakpoints);

        var requestedBreakpoint = Assert.Single(Assert.Single(client.Requests)
            .Arguments["breakpoints"]!
            .AsArray())!
            .AsObject();
        Assert.Equal("Speed > 0", requestedBreakpoint["condition"]!.GetValue<string>());
        Assert.Equal("3", requestedBreakpoint["hitCondition"]!.GetValue<string>());
        var result = Assert.Single(results);
        Assert.Equal(ScriptBreakpointBackendStatus.Applied, result.Status);
        Assert.True(result.Verified);
    }

    [Fact]
    public void ReplaceSourceBreakpoints_WhenConditionCapabilityIsMissing_SkipsUnsupportedBreakpoint()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var breakpoints = session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(12, 9, "Speed > 0") });
        var client = new FakeDapRequestClient();
        client.EnqueueResponse(new JsonObject
        {
            ["breakpoints"] = new JsonArray()
        });
        var backend = new DapScriptBreakpointBackend(client);

        var results = backend.ReplaceSourceBreakpoints(emit.DebugMap.SourceDocumentPath, breakpoints);

        Assert.Empty(Assert.Single(client.Requests).Arguments["breakpoints"]!.AsArray());
        var result = Assert.Single(results);
        Assert.Equal(ScriptBreakpointBackendStatus.Unsupported, result.Status);
        Assert.False(result.Verified);
        Assert.Contains("supportsConditionalBreakpoints", result.Message, StringComparison.Ordinal);
    }

    private static ScriptDebugSession CreateSession(DebugScriptEmitResult emit)
    {
        return new ScriptDebugSession(
            emit.DebugMap,
            File.ReadAllText(emit.DebugMap.SourceDocumentPath));
    }

    private static DebugScriptEmitResult EmitPlayerMove()
    {
        return DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            Path.Combine(
                Path.GetTempPath(),
                "ScriptLab.Tests",
                Guid.NewGuid().ToString("N")));
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

    private sealed class FakeDapRequestClient : IDapRequestClient
    {
        private readonly Queue<JsonObject> responses = new();

        public List<DapRequest> Requests { get; } = new();

        public void EnqueueResponse(JsonObject response)
        {
            responses.Enqueue(response);
        }

        public JsonObject SendRequest(string command, JsonObject arguments)
        {
            Requests.Add(new DapRequest(command, arguments.DeepClone().AsObject()));
            return responses.Dequeue();
        }
    }

    private sealed record DapRequest(string Command, JsonObject Arguments);
}
