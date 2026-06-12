using ScriptLab.Debug.Dap;
using ScriptLab;
using System.Text.Json.Nodes;

namespace ScriptLab.Tests;

public sealed class DapDebugSessionLauncherTests
{
    [Fact]
    public void Launch_WhenAdapterResponds_CreatesRuntimeAndSendsHandshakeRequests()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(12, 9) });
        var transport = new FakeDapTransport();
        transport.Events.Add(new JsonObject
        {
            ["type"] = "event",
            ["event"] = "initialized"
        });
        transport.EnqueueResponse(new JsonObject
        {
            ["supportsConditionalBreakpoints"] = true,
            ["supportsHitConditionalBreakpoints"] = true
        });
        transport.EnqueueResponse(new JsonObject());
        transport.EnqueueResponse(new JsonObject
        {
            ["breakpoints"] = new JsonArray
            {
                new JsonObject
                {
                    ["verified"] = true,
                    ["line"] = 12,
                    ["column"] = 9
                }
            }
        });
        transport.EnqueueResponse(new JsonObject());
        transport.RequireConfigurationDoneBeforePendingWait = true;
        var launcher = new DapDebugSessionLauncher(new DapDebugSessionClient(transport, transport));

        var result = launcher.Launch(
            session,
            emit.DebugMap,
            emit.DebugMap.SourceDocumentPath,
            new JsonObject
            {
                ["adapterID"] = "scriptlab-test"
            },
            new JsonObject
            {
                ["program"] = emit.AssemblyPath
            });

        Assert.NotNull(result.Runtime);
        Assert.Equal(DapDebugSessionPhase.Launched, result.Lifecycle.Phase);
        Assert.Equal(
            new[]
            {
                DapDebugSessionPhase.Created,
                DapDebugSessionPhase.Initialized,
                DapDebugSessionPhase.BreakpointsConfigured,
                DapDebugSessionPhase.ConfigurationDone,
                DapDebugSessionPhase.Launched
            },
            result.Lifecycle.CompletedPhases);
        Assert.True(result.Capabilities.SupportsConditionalBreakpoints);
        Assert.True(result.Capabilities.SupportsHitConditionalBreakpoints);
        Assert.True(result.InitializedEventReceived);
        var breakpoint = Assert.Single(result.BreakpointResults);
        Assert.Equal(ScriptBreakpointBackendStatus.Applied, breakpoint.Status);

        Assert.Equal(
            new[] { "initialize", "launch", "setBreakpoints", "configurationDone" },
            transport.Requests.Select(request => request.Command));
        Assert.Equal("scriptlab-test", transport.Requests[0].Arguments["adapterID"]!.GetValue<string>());
        Assert.Equal(emit.AssemblyPath, transport.Requests[1].Arguments["program"]!.GetValue<string>());
    }

    [Fact]
    public void Attach_WhenAdapterResponds_CreatesRuntimeAndSendsAttachHandshakeRequests()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(12, 9) });
        var transport = new FakeDapTransport();
        transport.Events.Add(new JsonObject
        {
            ["type"] = "event",
            ["event"] = "initialized"
        });
        transport.EnqueueResponse(new JsonObject
        {
            ["supportsBreakpointLocationsRequest"] = true
        });
        transport.EnqueueResponse(new JsonObject());
        transport.EnqueueResponse(new JsonObject
        {
            ["breakpoints"] = new JsonArray
            {
                new JsonObject
                {
                    ["verified"] = true,
                    ["line"] = 12,
                    ["column"] = 9
                }
            }
        });
        transport.EnqueueResponse(new JsonObject());
        transport.RequireConfigurationDoneBeforePendingWait = true;
        var launcher = new DapDebugSessionLauncher(new DapDebugSessionClient(transport, transport));

        var result = launcher.Attach(
            session,
            emit.DebugMap,
            emit.DebugMap.SourceDocumentPath,
            new JsonObject
            {
                ["adapterID"] = "scriptlab-test"
            },
            new JsonObject
            {
                ["processId"] = 4242,
                ["hostKind"] = "cppClr",
                ["terminateOnDisconnect"] = false
            });

        Assert.NotNull(result.Runtime);
        Assert.Equal(DapDebugSessionPhase.Attached, result.Lifecycle.Phase);
        Assert.Equal(
            new[]
            {
                DapDebugSessionPhase.Created,
                DapDebugSessionPhase.Initialized,
                DapDebugSessionPhase.BreakpointsConfigured,
                DapDebugSessionPhase.ConfigurationDone,
                DapDebugSessionPhase.Attached
            },
            result.Lifecycle.CompletedPhases);
        Assert.True(result.Capabilities.SupportsBreakpointLocationsRequest);
        Assert.True(result.InitializedEventReceived);
        var breakpoint = Assert.Single(result.BreakpointResults);
        Assert.Equal(ScriptBreakpointBackendStatus.Applied, breakpoint.Status);

        Assert.Equal(
            new[] { "initialize", "attach", "setBreakpoints", "configurationDone" },
            transport.Requests.Select(request => request.Command));
        Assert.Equal("scriptlab-test", transport.Requests[0].Arguments["adapterID"]!.GetValue<string>());
        Assert.Equal(4242, transport.Requests[1].Arguments["processId"]!.GetValue<int>());
        Assert.Equal("cppClr", transport.Requests[1].Arguments["hostKind"]!.GetValue<string>());
        Assert.False(transport.Requests[1].Arguments["terminateOnDisconnect"]!.GetValue<bool>());
    }

    private static ScriptDebugSession CreateSession(DebugScriptEmitResult emit)
    {
        return new ScriptDebugSession(
            emit.DebugMap,
            File.ReadAllText(emit.DebugMap.SourceDocumentPath),
            emit.DebugMap.SourceDocumentPath);
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

    private sealed class FakeDapTransport : IDapRequestClient, IDapPendingRequestClient, IDapEventSource
    {
        private readonly Queue<JsonObject> responses = new();

        public List<JsonObject> Events { get; } = new();

        public List<DapRequest> Requests { get; } = new();

        public bool RequireConfigurationDoneBeforePendingWait { get; set; }

        public void EnqueueResponse(JsonObject response)
        {
            responses.Enqueue(response);
        }

        public JsonObject SendRequest(string command, JsonObject arguments)
        {
            Requests.Add(new DapRequest(command, arguments.DeepClone().AsObject()));
            return responses.Dequeue();
        }

        public DapPendingRequest SendRequestPending(string command, JsonObject arguments)
        {
            Requests.Add(new DapRequest(command, arguments.DeepClone().AsObject()));
            var response = responses.Dequeue();
            return new DapPendingRequest(() =>
            {
                if (RequireConfigurationDoneBeforePendingWait)
                {
                    Assert.Contains(Requests, request => request.Command == "configurationDone");
                }

                return response;
            });
        }

        public IReadOnlyList<JsonObject> DrainEvents()
        {
            var events = Events.ToArray();
            Events.Clear();
            return events;
        }
    }

    private sealed record DapRequest(string Command, JsonObject Arguments);
}
