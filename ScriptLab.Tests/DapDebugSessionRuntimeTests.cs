using ScriptLab.Debug.Dap;
using ScriptLab;
using System.Text.Json.Nodes;

namespace ScriptLab.Tests;

public sealed class DapDebugSessionRuntimeTests
{
    [Fact]
    public void BackendSession_WhenCreated_ExposesRuntimeLifecycle()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var transport = new FakeDapTransport();
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        using var backend = new DapDebugBackendSession(runtime);

        Assert.Same(runtime, backend.Runtime);
        Assert.Equal(DapDebugSessionPhase.Created, backend.Lifecycle.Phase);
        Assert.Empty(backend.Lifecycle.CompletedPhases);
    }

    [Fact]
    public void Runtime_WhenBreakpointStopIsDrained_ReadsPausedSnapshotWithFrameVariables()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var host = DebugScriptHost.Load(emit);
        host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(12, 9) });
        var transport = new FakeDapTransport();
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
        transport.Events.Add(new JsonObject
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
        transport.EnqueueResponse(CreateStackTraceResponse(emit.DebugMap.SourceDocumentPath));
        transport.EnqueueResponse(CreateStackTraceResponse(emit.DebugMap.SourceDocumentPath));
        transport.EnqueueResponse(new JsonObject
        {
            ["scopes"] = new JsonArray
            {
                new JsonObject { ["name"] = "Arguments", ["variablesReference"] = 201 },
                new JsonObject { ["name"] = "Locals", ["variablesReference"] = 202 },
                new JsonObject { ["name"] = "This", ["variablesReference"] = 0 }
            }
        });
        transport.EnqueueResponse(new JsonObject
        {
            ["variables"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "delta",
                    ["value"] = "0.016",
                    ["type"] = "float",
                    ["variablesReference"] = 0
                }
            }
        });
        transport.EnqueueResponse(new JsonObject
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
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        var backendResult = Assert.Single(runtime.ApplySourceBreakpoints(emit.DebugMap.SourceDocumentPath));
        var stopped = Assert.Single(runtime.DrainStoppedEvents());
        var snapshot = runtime.ReadPausedSnapshot(host, stopped, entityId: 101);

        Assert.Equal(ScriptBreakpointBackendStatus.Applied, backendResult.Status);
        Assert.Equal(DapDebugSessionPhase.Stopped, runtime.Lifecycle.Phase);
        Assert.False(backendResult.Synthetic);
        Assert.Equal(ScriptStoppedEventStatus.Resolved, stopped.Status);
        Assert.Equal(11, stopped.ThreadId);

        var arguments = Assert.Single(snapshot.Scopes, scope => scope.Kind == ScriptDebugScopeKind.Arguments);
        Assert.True(arguments.Available);
        Assert.Equal("delta", Assert.Single(arguments.Variables).Name);

        var locals = Assert.Single(snapshot.Scopes, scope => scope.Kind == ScriptDebugScopeKind.Locals);
        Assert.True(locals.Available);
        Assert.Equal("amount", Assert.Single(locals.Variables).Name);

        Assert.Equal(new[] { "setBreakpoints", "stackTrace", "stackTrace", "scopes", "variables", "variables" },
            transport.Requests.Select(request => request.Command));
    }

    [Fact]
    public void Runtime_WhenNoEventsWereDrained_ReturnsNoStoppedEvents()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var transport = new FakeDapTransport();
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        Assert.Empty(runtime.DrainStoppedEvents());
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public void Runtime_WhenNoNewStoppedEventsAreDrained_KeepsCurrentStoppedEventValid()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var host = DebugScriptHost.Load(emit);
        host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        var transport = new FakeDapTransport();
        transport.Events.Add(new JsonObject
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
        transport.EnqueueResponse(CreateStackTraceResponse(emit.DebugMap.SourceDocumentPath));
        transport.EnqueueResponse(CreateStackTraceResponse(emit.DebugMap.SourceDocumentPath));
        transport.EnqueueResponse(new JsonObject
        {
            ["scopes"] = new JsonArray
            {
                new JsonObject { ["name"] = "Arguments", ["variablesReference"] = 0 },
                new JsonObject { ["name"] = "Locals", ["variablesReference"] = 0 },
                new JsonObject { ["name"] = "This", ["variablesReference"] = 0 }
            }
        });
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        var stopped = Assert.Single(runtime.DrainStoppedEvents());

        Assert.Empty(runtime.DrainStoppedEvents());
        var snapshot = runtime.ReadPausedSnapshot(host, stopped, entityId: 101);

        Assert.Equal(ScriptPausedSnapshotStatus.Partial, snapshot.Status);
        Assert.Equal(new[] { "stackTrace", "stackTrace", "scopes" },
            transport.Requests.Select(request => request.Command));
    }

    [Fact]
    public void Runtime_Disconnect_SendsDisconnectRequest()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var transport = new FakeDapTransport();
        transport.EnqueueResponse(new JsonObject());
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        runtime.Disconnect(terminateDebuggee: true);

        var request = Assert.Single(transport.Requests);
        Assert.Equal("disconnect", request.Command);
        Assert.True(request.Arguments["terminateDebuggee"]!.GetValue<bool>());
        Assert.Equal(DapDebugSessionPhase.Disconnected, runtime.Lifecycle.Phase);
    }

    [Fact]
    public void Runtime_DrainLifecycleEvents_ReturnsLifecycleEvents()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var transport = new FakeDapTransport();
        transport.Events.Add(new JsonObject
        {
            ["type"] = "event",
            ["event"] = "terminated"
        });
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        var lifecycleEvent = Assert.Single(runtime.DrainLifecycleEvents());

        Assert.Equal(DapLifecycleEventKind.Terminated, lifecycleEvent.Kind);
        Assert.Null(lifecycleEvent.ExitCode);
        Assert.False(lifecycleEvent.Restart);
    }

    [Fact]
    public void Runtime_DrainDebugEvents_WhenLifecycleFollowsStoppedEvent_InvalidatesStoppedEvent()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var host = DebugScriptHost.Load(emit);
        host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        var transport = new FakeDapTransport();
        transport.Events.Add(new JsonObject
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
        transport.Events.Add(new JsonObject
        {
            ["type"] = "event",
            ["event"] = "terminated"
        });
        transport.EnqueueResponse(CreateStackTraceResponse(emit.DebugMap.SourceDocumentPath));
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        var drain = runtime.DrainDebugEvents();
        var stopped = Assert.Single(drain.StoppedEvents);

        Assert.Null(drain.CurrentStoppedEvent);
        Assert.Equal(DapDebugSessionPhase.Terminated, runtime.Lifecycle.Phase);
        Assert.Equal(DapDebugSessionPhase.Terminated, drain.Lifecycle.Phase);
        Assert.Equal(DapLifecycleEventKind.Terminated, Assert.Single(drain.LifecycleEvents).Kind);
        var exception = Assert.Throws<InvalidOperationException>(
            () => runtime.ReadPausedSnapshot(host, stopped, entityId: 101));
        Assert.Contains("no longer current", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "stackTrace" }, transport.Requests.Select(request => request.Command));
    }

    [Fact]
    public void Runtime_ContinueAndNext_SendExecutionControlRequests()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var transport = new FakeDapTransport();
        transport.EnqueueResponse(new JsonObject
        {
            ["allThreadsContinued"] = true
        });
        transport.EnqueueResponse(new JsonObject());
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        var continueResult = runtime.Continue(threadId: 11);
        runtime.Next(threadId: 11);

        Assert.True(continueResult.AllThreadsContinued);
        Assert.Equal(DapDebugSessionPhase.Running, runtime.Lifecycle.Phase);
        Assert.Equal(new[] { "continue", "next" }, transport.Requests.Select(request => request.Command));
        Assert.Equal(11, transport.Requests[0].Arguments["threadId"]!.GetValue<int>());
        Assert.Equal(11, transport.Requests[1].Arguments["threadId"]!.GetValue<int>());
    }

    [Fact]
    public void Runtime_WhenContinueSucceeds_InvalidatesPreviousStoppedEvent()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var host = DebugScriptHost.Load(emit);
        host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        var transport = new FakeDapTransport();
        transport.Events.Add(new JsonObject
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
        transport.EnqueueResponse(CreateStackTraceResponse(emit.DebugMap.SourceDocumentPath));
        transport.EnqueueResponse(new JsonObject
        {
            ["allThreadsContinued"] = true
        });
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        var stopped = Assert.Single(runtime.DrainStoppedEvents());
        runtime.Continue(threadId: 11);

        var exception = Assert.Throws<InvalidOperationException>(
            () => runtime.ReadPausedSnapshot(host, stopped, entityId: 101));
        Assert.Contains("no longer current", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "stackTrace", "continue" }, transport.Requests.Select(request => request.Command));
    }

    [Fact]
    public void Runtime_WhenNextSucceeds_InvalidatesPreviousStoppedEvent()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var host = DebugScriptHost.Load(emit);
        host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        var transport = new FakeDapTransport();
        transport.Events.Add(new JsonObject
        {
            ["type"] = "event",
            ["event"] = "stopped",
            ["body"] = new JsonObject
            {
                ["reason"] = "step",
                ["threadId"] = 11,
                ["allThreadsStopped"] = true
            }
        });
        transport.EnqueueResponse(CreateStackTraceResponse(emit.DebugMap.SourceDocumentPath));
        transport.EnqueueResponse(new JsonObject());
        var runtime = new DapDebugSessionRuntime(
            new DapDebugSessionClient(transport, transport),
            session,
            emit.DebugMap);

        var stopped = Assert.Single(runtime.DrainStoppedEvents());
        runtime.Next(threadId: 11, granularity: "line");

        var exception = Assert.Throws<InvalidOperationException>(
            () => runtime.ReadPausedSnapshot(host, stopped, entityId: 101));
        Assert.Contains("no longer current", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "stackTrace", "next" }, transport.Requests.Select(request => request.Command));
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

    private sealed class FakeDapTransport : IDapRequestClient, IDapEventSource
    {
        private readonly Queue<JsonObject> responses = new();

        public List<JsonObject> Events { get; } = new();

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
            var events = Events.ToArray();
            Events.Clear();
            return events;
        }
    }

    private sealed record DapRequest(string Command, JsonObject Arguments);
}
