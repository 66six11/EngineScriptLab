using ScriptLab;
using System.Text.Json.Nodes;

namespace ScriptLab.Tests;

public sealed class DapScriptStoppedEventResolverTests
{
    [Fact]
    public void Resolve_WhenStoppedEventHasScriptFrame_ReturnsResolvedDebuggerStop()
    {
        var emit = EmitPlayerMove();
        var client = new FakeDapRequestClient();
        client.EnqueueResponse(new JsonObject
        {
            ["stackFrames"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 12,
                    ["name"] = "Update",
                    ["source"] = new JsonObject
                    {
                        ["path"] = emit.DebugMap.SourceDocumentPath
                    },
                    ["line"] = 12,
                    ["column"] = 9
                }
            }
        });
        var session = CreateSession(emit);
        var resolver = new DapScriptStoppedEventResolver(client, session);

        var stopped = resolver.Resolve(new DapStoppedEvent(
            ScriptStoppedReason.Breakpoint,
            ThreadId: 11,
            AllThreadsStopped: true,
            Description: null));

        Assert.Equal(ScriptStoppedEventStatus.Resolved, stopped.Status);
        Assert.Equal(ScriptStoppedReason.Breakpoint, stopped.Reason);
        Assert.False(stopped.Synthetic);
        Assert.Equal(11, stopped.ThreadId);
        Assert.True(stopped.AllThreadsStopped);
        Assert.Equal("Branch", stopped.Binding!.Candidates[0].Kind);
        Assert.Equal(12, stopped.Line);
        Assert.Equal(9, stopped.Column);
        Assert.Equal("stackTrace", Assert.Single(client.Requests).Command);
    }

    [Fact]
    public void Resolve_WhenStoppedEventHasNoThreadId_ReturnsUnresolvedWithoutDapRequests()
    {
        var emit = EmitPlayerMove();
        var client = new FakeDapRequestClient();
        var session = CreateSession(emit);
        var resolver = new DapScriptStoppedEventResolver(client, session);

        var stopped = resolver.Resolve(new DapStoppedEvent(
            ScriptStoppedReason.Breakpoint,
            ThreadId: null,
            AllThreadsStopped: false,
            Description: null));

        Assert.Equal(ScriptStoppedEventStatus.Unresolved, stopped.Status);
        Assert.False(stopped.Synthetic);
        Assert.Null(stopped.ThreadId);
        Assert.Contains("thread id", stopped.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public void Resolve_WhenStackTraceHasNoScriptFrame_ReturnsUnresolved()
    {
        var emit = EmitPlayerMove();
        var client = new FakeDapRequestClient();
        client.EnqueueResponse(new JsonObject
        {
            ["stackFrames"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 12,
                    ["name"] = "External",
                    ["source"] = new JsonObject
                    {
                        ["path"] = @"C:\Project\Other.cs"
                    },
                    ["line"] = 1,
                    ["column"] = 1
                }
            }
        });
        var session = CreateSession(emit);
        var resolver = new DapScriptStoppedEventResolver(client, session);

        var stopped = resolver.Resolve(new DapStoppedEvent(
            ScriptStoppedReason.Step,
            ThreadId: 11,
            AllThreadsStopped: false,
            Description: null));

        Assert.Equal(ScriptStoppedEventStatus.Unresolved, stopped.Status);
        Assert.Equal(ScriptStoppedReason.Step, stopped.Reason);
        Assert.Equal(11, stopped.ThreadId);
        Assert.Contains("current script source", stopped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDrainedStoppedEvents_WhenTransportHasStoppedEvent_ReturnsResolvedStop()
    {
        var emit = EmitPlayerMove();
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
        transport.EnqueueResponse(new JsonObject
        {
            ["stackFrames"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 12,
                    ["name"] = "Update",
                    ["source"] = new JsonObject
                    {
                        ["path"] = emit.DebugMap.SourceDocumentPath
                    },
                    ["line"] = 12,
                    ["column"] = 9
                }
            }
        });
        var session = CreateSession(emit);
        var resolver = new DapScriptStoppedEventResolver(
            new DapDebugSessionClient(transport, transport),
            session);

        var stopped = Assert.Single(resolver.ResolveDrainedStoppedEvents());

        Assert.Equal(ScriptStoppedEventStatus.Resolved, stopped.Status);
        Assert.Equal(11, stopped.ThreadId);
        Assert.Equal("Branch", stopped.Binding!.Candidates[0].Kind);
        Assert.Empty(transport.Events);
        Assert.Equal("stackTrace", Assert.Single(transport.Requests).Command);
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
