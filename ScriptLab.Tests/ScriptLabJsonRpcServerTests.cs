using ScriptLab;
using System.Diagnostics;
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
        var debugStateId = run.RootElement
            .GetProperty("result")
            .GetProperty("debugStateId")
            .GetInt32();
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

        using var variables = Send(
            server,
            5,
            "readVariables",
            new { debugStateId, scopeKind = ScriptDebugScopeKind.Inspector });
        var variablesResult = variables.RootElement.GetProperty("result");
        Assert.Equal("ok", variablesResult.GetProperty("status").GetString());
        Assert.Equal(debugStateId, variablesResult.GetProperty("debugStateId").GetInt32());
        Assert.Contains(
            variablesResult.GetProperty("variables").EnumerateArray(),
            variable => variable.GetProperty("name").GetString() == "Speed");

        using var trace = Send(server, 6, "getTraceSnapshot", new { });
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
        var bridgeManifestPath = @"D:\Game\Apps\SampleViewer\scriptlab.bridge.json";
        var enginePackageRoot = @"D:\Game\AshariaEngine";

        using var response = Send(
            server,
            1,
            "attachDebugHost",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                processId = 4242,
                hostKind = "cppClr",
                terminateOnDisconnect = false,
                bridgeManifestPath,
                enginePackageRoot
            });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal("unsupported", result.GetProperty("status").GetString());
        Assert.Equal("dap", result.GetProperty("backend").GetString());
        Assert.Equal("cppClr", result.GetProperty("hostKind").GetString());
        Assert.Equal(4242, result.GetProperty("processId").GetInt32());
        Assert.False(result.GetProperty("terminateOnDisconnect").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("lifecycle").ValueKind);
        var attachTarget = result.GetProperty("attachTarget");
        Assert.Equal("cppClr", attachTarget.GetProperty("hostKind").GetString());
        Assert.Equal(4242, attachTarget.GetProperty("processId").GetInt32());
        Assert.False(attachTarget.GetProperty("terminateOnDisconnect").GetBoolean());
        Assert.True(File.Exists(attachTarget.GetProperty("assemblyPath").GetString()));
        Assert.True(File.Exists(attachTarget.GetProperty("pdbPath").GetString()));
        Assert.True(File.Exists(attachTarget.GetProperty("debugMapPath").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(attachTarget.GetProperty("assemblyMvid").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(attachTarget.GetProperty("pdbId").GetString()));
        Assert.Equal(bridgeManifestPath, attachTarget.GetProperty("bridgeManifestPath").GetString());
        Assert.Equal(enginePackageRoot, attachTarget.GetProperty("enginePackageRoot").GetString());
        Assert.False(attachTarget.TryGetProperty("processStartTimeUtc", out _));
    }

    [Fact]
    public void HandleRequest_WhenDebugHostIsRegistered_AttachUsesRegisteredHostMetadata()
    {
        var server = CreateServer();
        var bridgeManifestPath = @"D:\Game\Apps\SampleViewer\scriptlab.bridge.json";
        var enginePackageRoot = @"D:\Game\AshariaEngine";

        using var none = Send(server, 1, "getRegisteredDebugHost", new { });
        Assert.Equal("none", none.RootElement.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, none.RootElement.GetProperty("result").GetProperty("host").ValueKind);

        using var registered = Send(
            server,
            2,
            "registerDebugHost",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                processId = 4242,
                hostKind = "cppClr",
                terminateOnDisconnect = true,
                bridgeManifestPath,
                enginePackageRoot
            });
        var registeredResult = registered.RootElement.GetProperty("result");
        Assert.Equal("registered", registeredResult.GetProperty("status").GetString());
        var host = registeredResult.GetProperty("host");
        Assert.Equal(4242, host.GetProperty("processId").GetInt32());
        Assert.True(host.GetProperty("terminateOnDisconnect").GetBoolean());
        Assert.Equal(bridgeManifestPath, host.GetProperty("bridgeManifestPath").GetString());
        Assert.Equal(enginePackageRoot, host.GetProperty("enginePackageRoot").GetString());

        using var current = Send(server, 3, "getRegisteredDebugHost", new { });
        Assert.Equal("registered", current.RootElement.GetProperty("result").GetProperty("status").GetString());

        using var attach = Send(server, 4, "attachDebugHost", new { });
        var attachResult = attach.RootElement.GetProperty("result");
        Assert.Equal("unsupported", attachResult.GetProperty("status").GetString());
        var attachTarget = attachResult.GetProperty("attachTarget");
        Assert.Equal(4242, attachTarget.GetProperty("processId").GetInt32());
        Assert.True(attachTarget.GetProperty("terminateOnDisconnect").GetBoolean());
        Assert.Equal(bridgeManifestPath, attachTarget.GetProperty("bridgeManifestPath").GetString());
        Assert.Equal(enginePackageRoot, attachTarget.GetProperty("enginePackageRoot").GetString());
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
                Array.Empty<ScriptBreakpointBackendResult>(),
                new DapDebugSessionLifecycle(
                    DapDebugSessionPhase.Attached,
                    new[]
                    {
                        DapDebugSessionPhase.Created,
                        DapDebugSessionPhase.Initialized,
                        DapDebugSessionPhase.ConfigurationDone,
                        DapDebugSessionPhase.Attached
                    }));
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
        Assert.Equal(DapDebugSessionPhase.Attached, attachResult
            .GetProperty("lifecycle")
            .GetProperty("phase")
            .GetString());
        Assert.NotNull(capturedRequest);
        Assert.Equal(4242, capturedRequest.AttachArguments["processId"]!.GetValue<int>());
        Assert.Equal("cppClr", capturedRequest.AttachArguments["hostKind"]!.GetValue<string>());
        Assert.Equal("scriptlab", capturedRequest.InitializeArguments["adapterID"]!.GetValue<string>());

        using var continued = Send(server, 2, "continue", new { threadId = 11 });
        var continueResult = continued.RootElement.GetProperty("result");

        Assert.Equal("continued", continueResult.GetProperty("status").GetString());
        Assert.Equal("dap", continueResult.GetProperty("backend").GetString());
        Assert.Equal(DapDebugSessionPhase.Running, continueResult
            .GetProperty("lifecycle")
            .GetProperty("phase")
            .GetString());

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
        Assert.Equal(DapDebugSessionPhase.Running, stepResult
            .GetProperty("lifecycle")
            .GetProperty("phase")
            .GetString());

        using var disconnected = Send(server, 4, "disconnectDebugHost", new { });
        var disconnectResult = disconnected.RootElement.GetProperty("result");

        Assert.Equal("disconnected", disconnectResult.GetProperty("status").GetString());
        Assert.Equal("dap", disconnectResult.GetProperty("backend").GetString());
        Assert.False(disconnectResult.GetProperty("terminateDebuggee").GetBoolean());
        Assert.Equal(DapDebugSessionPhase.Disconnected, disconnectResult
            .GetProperty("lifecycle")
            .GetProperty("phase")
            .GetString());

        Assert.Equal(new[] { "continue", "next", "disconnect" },
            runtimeTransport!.Requests.Select(request => request.Command));
        Assert.Equal(11, runtimeTransport.Requests[0].Arguments["threadId"]!.GetValue<int>());
        Assert.Equal(11, runtimeTransport.Requests[1].Arguments["threadId"]!.GetValue<int>());
        Assert.Equal("line", runtimeTransport.Requests[1].Arguments["granularity"]!.GetValue<string>());
        Assert.False(runtimeTransport.Requests[2].Arguments["terminateDebuggee"]!.GetValue<bool>());
    }

    [Fact]
    public void HandleRequest_WhenAttachedDapRuntimeDrainsStoppedEvent_ReturnsPausedSnapshot()
    {
        FakeDapTransport? runtimeTransport = null;
        var server = CreateServer(request =>
        {
            runtimeTransport = new FakeDapTransport();
            runtimeTransport.Events.Add(new JsonObject
            {
                ["type"] = "event",
                ["event"] = "stopped",
                ["body"] = new JsonObject
                {
                    ["reason"] = "breakpoint",
                    ["threadId"] = 11,
                    ["allThreadsStopped"] = true
                }
            });
            runtimeTransport.EnqueueResponse(CreateStackTraceResponse(request.DebugMap.SourceDocumentPath));
            runtimeTransport.EnqueueResponse(CreateStackTraceResponse(request.DebugMap.SourceDocumentPath));
            runtimeTransport.EnqueueResponse(new JsonObject
            {
                ["scopes"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Arguments", ["variablesReference"] = 0 },
                    new JsonObject { ["name"] = "Locals", ["variablesReference"] = 0 },
                    new JsonObject { ["name"] = "This", ["variablesReference"] = 0 }
                }
            });
            return new DapDebugSessionLaunchResult(
                new DapDebugSessionRuntime(
                    new DapDebugSessionClient(runtimeTransport, runtimeTransport),
                    request.Session,
                    request.DebugMap),
                new DapBreakpointBackendCapabilities(),
                InitializedEventReceived: false,
                Array.Empty<ScriptBreakpointBackendResult>(),
                new DapDebugSessionLifecycle(
                    DapDebugSessionPhase.Attached,
                    new[]
                    {
                        DapDebugSessionPhase.Created,
                        DapDebugSessionPhase.Initialized,
                        DapDebugSessionPhase.ConfigurationDone,
                        DapDebugSessionPhase.Attached
                    }));
        });

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

        using var response = Send(server, 2, "drainDebugEvents", new { entityId = 101 });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal("dap", result.GetProperty("backend").GetString());
        var stopped = Assert.Single(result.GetProperty("stoppedEvents").EnumerateArray());
        Assert.Equal("resolved", stopped.GetProperty("status").GetString());
        Assert.Equal(11, stopped.GetProperty("threadId").GetInt32());
        Assert.Equal(DapDebugSessionPhase.Stopped, result
            .GetProperty("lifecycle")
            .GetProperty("phase")
            .GetString());
        Assert.Equal("partial", result.GetProperty("pausedSnapshot").GetProperty("status").GetString());
        Assert.Equal(new[] { "stackTrace", "stackTrace", "scopes" },
            runtimeTransport!.Requests.Select(request => request.Command));

        using var paused = Send(server, 3, "getPausedSnapshot", new { });
        Assert.Equal("partial", paused.RootElement
            .GetProperty("result")
            .GetProperty("status")
            .GetString());
    }

    [Fact]
    public void HandleRequest_WhenDapEventArrivesWithoutDrain_BackgroundPumpCachesEvent()
    {
        FakeDapTransport? runtimeTransport = null;
        var server = CreateServer(request =>
        {
            runtimeTransport = new FakeDapTransport();
            runtimeTransport.EnqueueResponse(CreateStackTraceResponse(request.DebugMap.SourceDocumentPath));
            runtimeTransport.EnqueueResponse(CreateStackTraceResponse(request.DebugMap.SourceDocumentPath));
            runtimeTransport.EnqueueResponse(new JsonObject
            {
                ["scopes"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Arguments", ["variablesReference"] = 0 },
                    new JsonObject { ["name"] = "Locals", ["variablesReference"] = 0 },
                    new JsonObject { ["name"] = "This", ["variablesReference"] = 0 }
                }
            });
            return new DapDebugSessionLaunchResult(
                new DapDebugSessionRuntime(
                    new DapDebugSessionClient(runtimeTransport, runtimeTransport),
                    request.Session,
                    request.DebugMap),
                new DapBreakpointBackendCapabilities(),
                InitializedEventReceived: false,
                Array.Empty<ScriptBreakpointBackendResult>(),
                new DapDebugSessionLifecycle(
                    DapDebugSessionPhase.Attached,
                    new[]
                    {
                        DapDebugSessionPhase.Created,
                        DapDebugSessionPhase.Initialized,
                        DapDebugSessionPhase.ConfigurationDone,
                        DapDebugSessionPhase.Attached
                    }));
        });

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

        runtimeTransport!.AddEvent(new JsonObject
        {
            ["type"] = "event",
            ["event"] = "stopped",
            ["body"] = new JsonObject
            {
                ["reason"] = "breakpoint",
                ["threadId"] = 11,
                ["allThreadsStopped"] = true
            }
        });
        Assert.True(
            SpinWait.SpinUntil(
                () => runtimeTransport.RequestCommands.SequenceEqual(new[] { "stackTrace" }),
                TimeSpan.FromSeconds(2)));
        Assert.Equal(new[] { "stackTrace" }, runtimeTransport.RequestCommands);

        using var response = Send(server, 2, "drainDebugEvents", new { afterEventSequence = 0 });
        var result = response.RootElement.GetProperty("result");
        var stopped = Assert.Single(result.GetProperty("stoppedEvents").EnumerateArray());

        Assert.Equal(1, result.GetProperty("eventSequence").GetInt64());
        Assert.Equal("resolved", stopped.GetProperty("status").GetString());
        Assert.Equal("partial", result.GetProperty("pausedSnapshot").GetProperty("status").GetString());
        Assert.Equal(new[] { "stackTrace", "stackTrace", "scopes" },
            runtimeTransport.RequestCommands);
    }

    [Fact]
    public void HandleRequest_WhenAttachedDapRuntimeIsPaused_ReadVariablesUsesCurrentDebugState()
    {
        FakeDapTransport? runtimeTransport = null;
        var server = CreateServer(request =>
        {
            runtimeTransport = new FakeDapTransport();
            runtimeTransport.Events.Add(new JsonObject
            {
                ["type"] = "event",
                ["event"] = "stopped",
                ["body"] = new JsonObject
                {
                    ["reason"] = "breakpoint",
                    ["threadId"] = 11,
                    ["allThreadsStopped"] = true
                }
            });
            runtimeTransport.EnqueueResponse(CreateStackTraceResponse(request.DebugMap.SourceDocumentPath));
            runtimeTransport.EnqueueResponse(CreateStackTraceResponse(request.DebugMap.SourceDocumentPath));
            runtimeTransport.EnqueueResponse(new JsonObject
            {
                ["scopes"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Arguments", ["variablesReference"] = 201 },
                    new JsonObject { ["name"] = "Locals", ["variablesReference"] = 202 },
                    new JsonObject { ["name"] = "This", ["variablesReference"] = 0 }
                }
            });
            runtimeTransport.EnqueueResponse(new JsonObject
            {
                ["variables"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "delta",
                        ["value"] = "0.016",
                        ["type"] = "float",
                        ["variablesReference"] = 301
                    }
                }
            });
            runtimeTransport.EnqueueResponse(new JsonObject
            {
                ["variables"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "amount",
                        ["value"] = "0.128",
                        ["type"] = "float",
                        ["variablesReference"] = 0
                    }
                }
            });
            runtimeTransport.EnqueueResponse(new JsonObject
            {
                ["variables"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "value",
                        ["value"] = "expanded",
                        ["type"] = "string",
                        ["variablesReference"] = 0
                    }
                }
            });
            runtimeTransport.EnqueueResponse(new JsonObject
            {
                ["allThreadsContinued"] = true
            });
            return new DapDebugSessionLaunchResult(
                new DapDebugSessionRuntime(
                    new DapDebugSessionClient(runtimeTransport, runtimeTransport),
                    request.Session,
                    request.DebugMap),
                new DapBreakpointBackendCapabilities(),
                InitializedEventReceived: false,
                Array.Empty<ScriptBreakpointBackendResult>(),
                new DapDebugSessionLifecycle(
                    DapDebugSessionPhase.Attached,
                    new[]
                    {
                        DapDebugSessionPhase.Created,
                        DapDebugSessionPhase.Initialized,
                        DapDebugSessionPhase.ConfigurationDone,
                        DapDebugSessionPhase.Attached
                    }));
        });

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

        using var stopped = Send(server, 2, "drainDebugEvents", new { entityId = 101 });
        var stoppedResult = stopped.RootElement.GetProperty("result");
        var debugStateId = stoppedResult.GetProperty("debugStateId").GetInt32();

        using var arguments = Send(
            server,
            3,
            "readVariables",
            new { debugStateId, scopeKind = ScriptDebugScopeKind.Arguments });
        var argumentsResult = arguments.RootElement.GetProperty("result");
        var delta = Assert.Single(argumentsResult.GetProperty("variables").EnumerateArray());
        Assert.Equal("ok", argumentsResult.GetProperty("status").GetString());
        Assert.Equal("delta", delta.GetProperty("name").GetString());
        Assert.Equal(301, delta.GetProperty("variablesReference").GetInt32());

        using var child = Send(
            server,
            4,
            "readVariables",
            new { debugStateId, variablesReference = 301 });
        var childResult = child.RootElement.GetProperty("result");
        var value = Assert.Single(childResult.GetProperty("variables").EnumerateArray());
        Assert.Equal("ok", childResult.GetProperty("status").GetString());
        Assert.Equal("value", value.GetProperty("name").GetString());
        Assert.Equal("expanded", value.GetProperty("displayValue").GetString());

        Send(server, 5, "continue", new { threadId = 11 }).Dispose();
        using var stale = Send(
            server,
            6,
            "readVariables",
            new { debugStateId, scopeKind = ScriptDebugScopeKind.Arguments });
        Assert.Equal("stale", stale.RootElement
            .GetProperty("result")
            .GetProperty("status")
            .GetString());

        Assert.Equal(
            new[] { "stackTrace", "stackTrace", "scopes", "variables", "variables", "variables", "continue" },
            runtimeTransport!.Requests.Select(request => request.Command));
    }

    [Fact]
    public void HandleRequest_WhenAttachedDapRuntimeDrainsStoppedThenLifecycleEvent_ReturnsEventsWithoutPausedSnapshot()
    {
        FakeDapTransport? runtimeTransport = null;
        var server = CreateServer(request =>
        {
            runtimeTransport = new FakeDapTransport();
            runtimeTransport.Events.Add(new JsonObject
            {
                ["type"] = "event",
                ["event"] = "stopped",
                ["body"] = new JsonObject
                {
                    ["reason"] = "breakpoint",
                    ["threadId"] = 11,
                    ["allThreadsStopped"] = true
                }
            });
            runtimeTransport.Events.Add(new JsonObject
            {
                ["type"] = "event",
                ["event"] = "terminated"
            });
            runtimeTransport.EnqueueResponse(CreateStackTraceResponse(request.DebugMap.SourceDocumentPath));
            return new DapDebugSessionLaunchResult(
                new DapDebugSessionRuntime(
                    new DapDebugSessionClient(runtimeTransport, runtimeTransport),
                    request.Session,
                    request.DebugMap),
                new DapBreakpointBackendCapabilities(),
                InitializedEventReceived: false,
                Array.Empty<ScriptBreakpointBackendResult>(),
                new DapDebugSessionLifecycle(
                    DapDebugSessionPhase.Attached,
                    new[]
                    {
                        DapDebugSessionPhase.Created,
                        DapDebugSessionPhase.Initialized,
                        DapDebugSessionPhase.ConfigurationDone,
                        DapDebugSessionPhase.Attached
                    }));
        });

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

        using var response = Send(server, 2, "drainDebugEvents", new { entityId = 101 });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal("dap", result.GetProperty("backend").GetString());
        Assert.Equal("resolved", Assert.Single(result.GetProperty("stoppedEvents").EnumerateArray())
            .GetProperty("status")
            .GetString());
        Assert.Equal(DapLifecycleEventKind.Terminated, Assert.Single(result.GetProperty("lifecycleEvents").EnumerateArray())
            .GetProperty("kind")
            .GetString());
        Assert.Equal(DapDebugSessionPhase.Terminated, result
            .GetProperty("lifecycle")
            .GetProperty("phase")
            .GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("pausedSnapshot").ValueKind);
        Assert.Equal(new[] { "stackTrace" },
            runtimeTransport!.Requests.Select(request => request.Command));

        using var paused = Send(server, 3, "getPausedSnapshot", new { });
        Assert.Equal(JsonValueKind.Null, paused.RootElement.GetProperty("result").ValueKind);
    }

    [Fact]
    public void HandleRequest_WhenAttachedDapRuntimeDrainsBreakpointEvent_ReturnsBreakpointEvents()
    {
        var server = CreateServer(request =>
        {
            var runtimeTransport = new FakeDapTransport();
            runtimeTransport.Events.Add(new JsonObject
            {
                ["type"] = "event",
                ["event"] = "breakpoint",
                ["body"] = new JsonObject
                {
                    ["reason"] = "changed",
                    ["breakpoint"] = new JsonObject
                    {
                        ["verified"] = true,
                        ["line"] = 14,
                        ["column"] = 13,
                        ["source"] = new JsonObject
                        {
                            ["path"] = request.DebugMap.SourceDocumentPath
                        }
                    }
                }
            });
            return new DapDebugSessionLaunchResult(
                new DapDebugSessionRuntime(
                    new DapDebugSessionClient(runtimeTransport, runtimeTransport),
                    request.Session,
                    request.DebugMap),
                new DapBreakpointBackendCapabilities(),
                InitializedEventReceived: false,
                Array.Empty<ScriptBreakpointBackendResult>(),
                new DapDebugSessionLifecycle(
                    DapDebugSessionPhase.Attached,
                    new[]
                    {
                        DapDebugSessionPhase.Created,
                        DapDebugSessionPhase.Initialized,
                        DapDebugSessionPhase.ConfigurationDone,
                        DapDebugSessionPhase.Attached
                    }));
        });

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

        using var response = Send(server, 2, "drainDebugEvents", new { });
        var result = response.RootElement.GetProperty("result");
        var breakpointEvent = Assert.Single(result.GetProperty("breakpointEvents").EnumerateArray());

        Assert.Equal(1, result.GetProperty("eventSequence").GetInt64());
        Assert.Equal(2, result.GetProperty("nextEventSequence").GetInt64());
        Assert.Equal(1, result.GetProperty("earliestEventSequence").GetInt64());
        Assert.Equal("changed", breakpointEvent.GetProperty("reason").GetString());
        Assert.True(breakpointEvent.GetProperty("verified").GetBoolean());
        Assert.Equal(14, breakpointEvent.GetProperty("line").GetInt32());
        Assert.Equal(13, breakpointEvent.GetProperty("column").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("pausedSnapshot").ValueKind);

        using var replay = Send(server, 3, "drainDebugEvents", new { afterEventSequence = 0 });
        var replayResult = replay.RootElement.GetProperty("result");
        var replayedBreakpointEvent = Assert.Single(replayResult.GetProperty("breakpointEvents").EnumerateArray());
        Assert.Equal(1, replayResult.GetProperty("eventSequence").GetInt64());
        Assert.Equal(2, replayResult.GetProperty("nextEventSequence").GetInt64());
        Assert.True(replayedBreakpointEvent.GetProperty("verified").GetBoolean());

        using var empty = Send(server, 4, "drainDebugEvents", new { afterEventSequence = 1 });
        var emptyResult = empty.RootElement.GetProperty("result");
        Assert.Equal(JsonValueKind.Null, emptyResult.GetProperty("eventSequence").ValueKind);
        Assert.Equal(2, emptyResult.GetProperty("nextEventSequence").GetInt64());
        Assert.Equal(1, emptyResult.GetProperty("earliestEventSequence").GetInt64());
        Assert.Empty(emptyResult.GetProperty("breakpointEvents").EnumerateArray());
    }

    [Fact]
    public async Task HandleRequest_WhenDrainDebugEventsHasTimeout_WaitsForLaterDapEvent()
    {
        FakeDapTransport? runtimeTransport = null;
        var server = CreateServer(request =>
        {
            runtimeTransport = new FakeDapTransport();
            return new DapDebugSessionLaunchResult(
                new DapDebugSessionRuntime(
                    new DapDebugSessionClient(runtimeTransport, runtimeTransport),
                    request.Session,
                    request.DebugMap),
                new DapBreakpointBackendCapabilities(),
                InitializedEventReceived: false,
                Array.Empty<ScriptBreakpointBackendResult>(),
                new DapDebugSessionLifecycle(
                    DapDebugSessionPhase.Attached,
                    new[]
                    {
                        DapDebugSessionPhase.Created,
                        DapDebugSessionPhase.Initialized,
                        DapDebugSessionPhase.ConfigurationDone,
                        DapDebugSessionPhase.Attached
                    }));
        });

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

        var responseTask = Task.Run(() => Send(
            server,
            2,
            "drainDebugEvents",
            new { timeoutMilliseconds = 1000 }));
        await Task.Delay(50);
        runtimeTransport!.AddEvent(new JsonObject
        {
            ["type"] = "event",
            ["event"] = "breakpoint",
            ["body"] = new JsonObject
            {
                ["reason"] = "changed",
                ["breakpoint"] = new JsonObject
                {
                    ["verified"] = true,
                    ["line"] = 14,
                    ["column"] = 13,
                    ["source"] = new JsonObject
                    {
                        ["path"] = GetSamplePath("PlayerMove.ash.cs")
                    }
                }
            }
        });

        using var response = await responseTask;
        var result = response.RootElement.GetProperty("result");
        var breakpointEvent = Assert.Single(result.GetProperty("breakpointEvents").EnumerateArray());

        Assert.Equal("dap", result.GetProperty("backend").GetString());
        Assert.True(breakpointEvent.GetProperty("verified").GetBoolean());
        Assert.Equal(14, breakpointEvent.GetProperty("line").GetInt32());
    }

    [Fact]
    public void HandleRequest_WhenDrainDebugEventsHasNoDapRuntime_ReturnsEmptyEvents()
    {
        var server = CreateServer();

        using var response = Send(server, 1, "drainDebugEvents", new { });
        var result = response.RootElement.GetProperty("result");

        Assert.Equal("probe", result.GetProperty("backend").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("eventSequence").ValueKind);
        Assert.Equal(1, result.GetProperty("nextEventSequence").GetInt64());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("earliestEventSequence").ValueKind);
        Assert.Empty(result.GetProperty("stoppedEvents").EnumerateArray());
        Assert.Empty(result.GetProperty("lifecycleEvents").EnumerateArray());
        Assert.Empty(result.GetProperty("breakpointEvents").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("stoppedEvent").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("pausedSnapshot").ValueKind);
    }

    [Fact]
    public void HandleRequest_WhenWaitingForDebugHostExit_ObservesAttachedProcessWithoutOwningIt()
    {
        var server = CreateServer();

        using var notAttached = Send(server, 1, "waitDebugHostExit", new { timeoutMilliseconds = 0 });
        Assert.Equal("notAttached", notAttached.RootElement
            .GetProperty("result")
            .GetProperty("status")
            .GetString());

        using var attach = Send(
            server,
            2,
            "attachDebugHost",
            new
            {
                scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                processId = Process.GetCurrentProcess().Id,
                hostKind = "cppClr"
            });
        var attachTarget = attach.RootElement
            .GetProperty("result")
            .GetProperty("attachTarget");
        Assert.False(string.IsNullOrWhiteSpace(attachTarget
            .GetProperty("processStartTimeUtc")
            .GetString()));

        using var running = Send(server, 3, "waitDebugHostExit", new { timeoutMilliseconds = 0 });
        var runningResult = running.RootElement.GetProperty("result");

        Assert.Equal("running", runningResult.GetProperty("status").GetString());
        Assert.Equal("dap", runningResult.GetProperty("backend").GetString());
        Assert.Equal(Process.GetCurrentProcess().Id, runningResult.GetProperty("processId").GetInt32());
        Assert.Equal(JsonValueKind.Null, runningResult.GetProperty("exitCode").ValueKind);
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
        var processStartTimeUtc = new DateTimeOffset(2026, 5, 23, 10, 11, 12, TimeSpan.Zero);
        var attachTarget = new ScriptLabDapAttachTarget(
            "cppClr",
            ProcessId: 4242,
            TerminateOnDisconnect: false,
            AssemblyPath: @"D:\Game\Scripts\PlayerMove.dll",
            PdbPath: @"D:\Game\Scripts\PlayerMove.pdb",
            DebugMapPath: @"D:\Game\Scripts\PlayerMove.debugmap.json",
            AssemblyMvid: "mvid-1",
            PdbId: "pdb-1",
            ProcessStartTimeUtc: processStartTimeUtc,
            BridgeManifestPath: @"D:\Game\Apps\SampleViewer\scriptlab.bridge.json",
            EnginePackageRoot: @"D:\Game\AshariaEngine");

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
        Assert.Equal(processStartTimeUtc, arguments["processStartTimeUtc"]!.GetValue<DateTimeOffset>());
        Assert.Equal(
            @"D:\Game\Apps\SampleViewer\scriptlab.bridge.json",
            arguments["bridgeManifestPath"]!.GetValue<string>());
        Assert.Equal(@"D:\Game\AshariaEngine", arguments["enginePackageRoot"]!.GetValue<string>());
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

    private static JsonObject CreateStackTraceResponse(string sourcePath)
    {
        return new JsonObject
        {
            ["stackFrames"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 101,
                    ["name"] = "Update",
                    ["source"] = new JsonObject
                    {
                        ["path"] = sourcePath
                    },
                    ["line"] = 12,
                    ["column"] = 9
                }
            }
        };
    }

    private sealed class FakeDapTransport : IDapRequestClient, IDapEventSource
    {
        private readonly object syncRoot = new();
        private readonly Queue<JsonObject> responses = new();

        public List<JsonObject> Events { get; } = new();

        public List<DapRequest> Requests { get; } = new();

        public IReadOnlyList<string> RequestCommands
        {
            get
            {
                lock (syncRoot)
                {
                    return Requests.Select(request => request.Command).ToArray();
                }
            }
        }

        public void EnqueueResponse(JsonObject response)
        {
            lock (syncRoot)
            {
                responses.Enqueue(response);
            }
        }

        public void AddEvent(JsonObject message)
        {
            lock (Events)
            {
                Events.Add(message);
            }
        }

        public JsonObject SendRequest(string command, JsonObject arguments)
        {
            lock (syncRoot)
            {
                Requests.Add(new DapRequest(command, arguments.DeepClone().AsObject()));
                return responses.Dequeue();
            }
        }

        public bool HasRequest(string command)
        {
            lock (syncRoot)
            {
                return Requests.Any(request => request.Command == command);
            }
        }

        public IReadOnlyList<JsonObject> DrainEvents()
        {
            lock (Events)
            {
                var events = Events.ToArray();
                Events.Clear();
                return events;
            }
        }
    }

    private sealed record DapRequest(string Command, JsonObject Arguments);
}
