using ScriptLab;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        Assert.False(backend.GetProperty("verified").GetBoolean());
        Assert.True(backend.GetProperty("synthetic").GetBoolean());

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
        Assert.False(backend.GetProperty("verified").GetBoolean());
        Assert.True(backend.GetProperty("synthetic").GetBoolean());
        Assert.Equal("Branch", result
            .GetProperty("stoppedEvent")
            .GetProperty("binding")
            .GetProperty("candidates")[0]
            .GetProperty("kind")
            .GetString());
    }

    [Fact]
    public void HandleRequest_WhenContinueIsCalledOnProbeBackend_ReturnsUnsupportedAndClearsPausedSnapshot()
    {
        var server = CreateServer();

        using var run = Send(
            server,
            1,
            "runDebug",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                graphNodeId = "n3"
            });
        Assert.Equal("partial", run.RootElement
            .GetProperty("result")
            .GetProperty("pausedSnapshot")
            .GetProperty("status")
            .GetString());

        using var response = Send(server, 2, "continue", new { threadId = 11 });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal("unsupported", result.GetProperty("status").GetString());
        Assert.Equal("probe", result.GetProperty("backend").GetString());
        Assert.Equal(11, result.GetProperty("threadId").GetInt32());

        using var paused = Send(server, 3, "getPausedSnapshot", new { });
        Assert.Equal(JsonValueKind.Null, paused.RootElement.GetProperty("result").ValueKind);
    }

    [Fact]
    public void HandleRequest_WhenStepIsCalledOnProbeBackend_ReturnsUnsupportedAndClearsPausedSnapshot()
    {
        var server = CreateServer();

        Send(
            server,
            1,
            "runDebug",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                graphNodeId = "n3"
            }).Dispose();

        using var response = Send(
            server,
            2,
            "step",
            new
            {
                threadId = 11,
                kind = "next",
                granularity = "line"
            });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal("unsupported", result.GetProperty("status").GetString());
        Assert.Equal("probe", result.GetProperty("backend").GetString());
        Assert.Equal(11, result.GetProperty("threadId").GetInt32());
        Assert.Equal("next", result.GetProperty("stepKind").GetString());
        Assert.Equal("line", result.GetProperty("granularity").GetString());

        using var paused = Send(server, 3, "getPausedSnapshot", new { });
        Assert.Equal(JsonValueKind.Null, paused.RootElement.GetProperty("result").ValueKind);
    }

    [Fact]
    public void HandleRequest_WhenAttachDebugHostIsCalled_RecordsCppClrHostTargetAsUnsupported()
    {
        var server = CreateServer();

        using var response = Send(
            server,
            1,
            "attachDebugHost",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                processId = 4242,
                hostKind = "cppClr",
                terminateOnDisconnect = false
            });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal("unsupported", result.GetProperty("status").GetString());
        Assert.Equal("dap", result.GetProperty("backend").GetString());
        Assert.Equal("cppClr", result.GetProperty("hostKind").GetString());
        Assert.Equal(4242, result.GetProperty("processId").GetInt32());
        Assert.False(result.GetProperty("terminateOnDisconnect").GetBoolean());
        var attachTarget = result.GetProperty("attachTarget");
        Assert.Equal("cppClr", attachTarget.GetProperty("hostKind").GetString());
        Assert.Equal(4242, attachTarget.GetProperty("processId").GetInt32());
        Assert.False(attachTarget.GetProperty("terminateOnDisconnect").GetBoolean());
        Assert.True(File.Exists(attachTarget.GetProperty("assemblyPath").GetString()));
        Assert.True(File.Exists(attachTarget.GetProperty("pdbPath").GetString()));
        Assert.True(File.Exists(attachTarget.GetProperty("debugMapPath").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(attachTarget.GetProperty("assemblyMvid").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(attachTarget.GetProperty("pdbId").GetString()));
    }

    [Fact]
    public void HandleRequest_WhenAttachDebugHostHasDapFactory_AttachesAndControlsDapRuntime()
    {
        ScriptLabDapAttachRequest? capturedRequest = null;
        FakeDapTransport? runtimeTransport = null;
        var server = CreateServer(request =>
        {
            capturedRequest = request;
            runtimeTransport = new FakeDapTransport();
            runtimeTransport.EnqueueResponse(new JsonObject
            {
                ["allThreadsContinued"] = true
            });
            runtimeTransport.EnqueueResponse(new JsonObject());
            runtimeTransport.EnqueueResponse(new JsonObject());
            return new DapDebugSessionLaunchResult(
                new DapDebugSessionRuntime(
                    new DapDebugSessionClient(runtimeTransport, runtimeTransport),
                    request.Session,
                    request.DebugMap),
                new DapBreakpointBackendCapabilities(
                    SupportsConditionalBreakpoints: false,
                    SupportsHitConditionalBreakpoints: false,
                    SupportsBreakpointLocationsRequest: true,
                    SupportsInstructionBreakpoints: false),
                InitializedEventReceived: false,
                Array.Empty<ScriptBreakpointBackendResult>());
        });

        using var attach = Send(
            server,
            1,
            "attachDebugHost",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                processId = 4242,
                hostKind = "cppClr"
            });
        var attachResult = attach.RootElement.GetProperty("result");

        Assert.Equal("attached", attachResult.GetProperty("status").GetString());
        Assert.Equal("dap", attachResult.GetProperty("backend").GetString());
        Assert.NotNull(capturedRequest);
        Assert.Equal(4242, capturedRequest.AttachArguments["processId"]!.GetValue<int>());
        Assert.Equal("cppClr", capturedRequest.AttachArguments["hostKind"]!.GetValue<string>());
        Assert.Equal("scriptlab", capturedRequest.InitializeArguments["adapterID"]!.GetValue<string>());

        using var continued = Send(server, 2, "continue", new { threadId = 11 });
        var continueResult = continued.RootElement.GetProperty("result");

        Assert.Equal("continued", continueResult.GetProperty("status").GetString());
        Assert.Equal("dap", continueResult.GetProperty("backend").GetString());

        using var stepped = Send(
            server,
            3,
            "step",
            new
            {
                threadId = 11,
                kind = "next",
                granularity = "line"
            });
        var stepResult = stepped.RootElement.GetProperty("result");

        Assert.Equal("stepped", stepResult.GetProperty("status").GetString());
        Assert.Equal("dap", stepResult.GetProperty("backend").GetString());

        using var disconnected = Send(server, 4, "disconnectDebugHost", new { });
        var disconnectResult = disconnected.RootElement.GetProperty("result");

        Assert.Equal("disconnected", disconnectResult.GetProperty("status").GetString());
        Assert.Equal("dap", disconnectResult.GetProperty("backend").GetString());
        Assert.False(disconnectResult.GetProperty("terminateDebuggee").GetBoolean());

        Assert.Equal(new[] { "continue", "next", "disconnect" },
            runtimeTransport!.Requests.Select(request => request.Command));
        Assert.Equal(11, runtimeTransport.Requests[0].Arguments["threadId"]!.GetValue<int>());
        Assert.Equal(11, runtimeTransport.Requests[1].Arguments["threadId"]!.GetValue<int>());
        Assert.Equal("line", runtimeTransport.Requests[1].Arguments["granularity"]!.GetValue<string>());
        Assert.False(runtimeTransport.Requests[2].Arguments["terminateDebuggee"]!.GetValue<bool>());
    }

    [Fact]
    public void HandleRequest_WhenContinuingRecordedCppClrHost_ReturnsDapUnsupported()
    {
        var server = CreateServer();

        Send(
            server,
            1,
            "attachDebugHost",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                processId = 4242,
                hostKind = "cppClr"
            }).Dispose();

        using var response = Send(server, 2, "continue", new { threadId = 11 });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal("unsupported", result.GetProperty("status").GetString());
        Assert.Equal("dap", result.GetProperty("backend").GetString());
        Assert.Contains(
            "no DAP debug session",
            result.GetProperty("reason").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void HandleRequest_WhenDisconnectDebugHostHasRecordedTarget_ClearsTargetWithoutTerminating()
    {
        var server = CreateServer();

        Send(
            server,
            1,
            "attachDebugHost",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                processId = 4242,
                hostKind = "cppClr"
            }).Dispose();

        using var disconnected = Send(server, 2, "disconnectDebugHost", new { });
        var disconnectResult = disconnected.RootElement.GetProperty("result");

        Assert.Equal("detached", disconnectResult.GetProperty("status").GetString());
        Assert.Equal("dap", disconnectResult.GetProperty("backend").GetString());
        Assert.False(disconnectResult.GetProperty("terminateDebuggee").GetBoolean());

        using var continued = Send(server, 3, "continue", new { threadId = 11 });
        var continueResult = continued.RootElement.GetProperty("result");

        Assert.Equal("unsupported", continueResult.GetProperty("status").GetString());
        Assert.Equal("probe", continueResult.GetProperty("backend").GetString());
    }

    [Fact]
    public void HandleRequest_WhenAttachDebugHostHasInvalidProcessId_ReturnsJsonRpcError()
    {
        var server = CreateServer();

        using var response = Send(
            server,
            1,
            "attachDebugHost",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                processId = 0
            });

        Assert.Equal(-32000, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains(
            "processId",
            response.RootElement.GetProperty("error").GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void HandleRequest_WhenAttachDebugHostHasUnsupportedHostKind_ReturnsJsonRpcError()
    {
        var server = CreateServer();

        using var response = Send(
            server,
            1,
            "attachDebugHost",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                processId = 4242,
                hostKind = "dotnet"
            });

        Assert.Equal(-32000, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains(
            "cppClr",
            response.RootElement.GetProperty("error").GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDapAttachArguments_WhenTargetIsProvided_ReturnsStableAttachShape()
    {
        var attachTarget = new ScriptLabDapAttachTarget(
            "cppClr",
            ProcessId: 4242,
            TerminateOnDisconnect: false,
            AssemblyPath: @"D:\Game\Scripts\PlayerMove.dll",
            PdbPath: @"D:\Game\Scripts\PlayerMove.pdb",
            DebugMapPath: @"D:\Game\Scripts\PlayerMove.debugmap.json",
            AssemblyMvid: "mvid-1",
            PdbId: "pdb-1");

        var arguments = ScriptLabJsonRpcServer.CreateDapAttachArguments(attachTarget);
        attachTarget = attachTarget with { ProcessId = 1 };

        Assert.Equal(4242, arguments["processId"]!.GetValue<int>());
        Assert.Equal("cppClr", arguments["hostKind"]!.GetValue<string>());
        Assert.False(arguments["terminateOnDisconnect"]!.GetValue<bool>());
        Assert.Equal(@"D:\Game\Scripts\PlayerMove.dll", arguments["assemblyPath"]!.GetValue<string>());
        Assert.Equal(@"D:\Game\Scripts\PlayerMove.pdb", arguments["pdbPath"]!.GetValue<string>());
        Assert.Equal(@"D:\Game\Scripts\PlayerMove.debugmap.json", arguments["debugMapPath"]!.GetValue<string>());
        Assert.Equal("mvid-1", arguments["assemblyMvid"]!.GetValue<string>());
        Assert.Equal("pdb-1", arguments["pdbId"]!.GetValue<string>());
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

    private static ScriptLabJsonRpcServer CreateServer(
        Func<ScriptLabDapAttachRequest, DapDebugSessionLaunchResult>? dapAttachFactory = null)
    {
        return new ScriptLabJsonRpcServer(
            new ScriptLabServerOptions(CreateOutputDirectory()),
            dapAttachFactory);
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

    private sealed class FakeDapTransport : IDapRequestClient, IDapEventSource
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

        public IReadOnlyList<JsonObject> DrainEvents()
        {
            return Array.Empty<JsonObject>();
        }
    }

    private sealed record DapRequest(string Command, JsonObject Arguments);
}
