using ScriptLab.Debug.Dap;
using ScriptLab;
using System.Text.Json.Nodes;

namespace ScriptLab.Tests;

public sealed class DapScriptFrameVariableBackendTests
{
    [Fact]
    public void ReadVariables_WhenStoppedEventHasThreadId_ReadsArgumentsLocalsAndThisScopes()
    {
        var emit = EmitPlayerMove();
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");
        var session = CreateSession(emit);
        var stopped = session.ResolveStoppedProbe(branchProbe.ProbeId, synthetic: false, threadId: 11);
        var client = new FakeDapRequestClient();
        client.EnqueueResponse(new JsonObject
        {
            ["stackFrames"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 101,
                    ["name"] = "Update",
                    ["source"] = new JsonObject
                    {
                        ["path"] = emit.DebugMap.SourceDocumentPath
                    },
                    ["line"] = stopped.Line,
                    ["column"] = stopped.Column
                }
            }
        });
        client.EnqueueResponse(new JsonObject
        {
            ["scopes"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Arguments",
                    ["variablesReference"] = 201
                },
                new JsonObject
                {
                    ["name"] = "Locals",
                    ["variablesReference"] = 202
                },
                new JsonObject
                {
                    ["name"] = "This",
                    ["variablesReference"] = 203
                }
            }
        });
        client.EnqueueResponse(new JsonObject
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
        client.EnqueueResponse(new JsonObject
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
        client.EnqueueResponse(new JsonObject
        {
            ["variables"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Speed",
                    ["value"] = "8.5",
                    ["type"] = "float",
                    ["variablesReference"] = 0
                }
            }
        });
        var backend = new DapScriptFrameVariableBackend(client, emit.DebugMap);

        var snapshot = backend.ReadVariables(stopped);

        Assert.True(snapshot.Available);
        var delta = Assert.Single(snapshot.Arguments);
        Assert.Equal("delta", delta.Name);
        Assert.Equal("0.016", delta.DisplayValue);
        Assert.Equal("float", delta.Type);
        Assert.Null(delta.RawValue);

        var amount = Assert.Single(snapshot.Locals);
        Assert.Equal("amount", amount.Name);
        Assert.Equal("dap:locals:202:amount", amount.VariableId);
        Assert.Null(amount.VariablesReference);

        var speed = Assert.Single(snapshot.ThisVariables);
        Assert.Equal("Speed", speed.Name);
        Assert.Equal("com.game.PlayerMove", speed.BehaviorId);
        Assert.Equal("Speed", speed.FieldId);

        Assert.Equal(new[] { "stackTrace", "scopes", "variables", "variables", "variables" }, client.Requests.Select(request => request.Command));
        Assert.Equal(11, client.Requests[0].Arguments["threadId"]!.GetValue<int>());
        Assert.Equal(101, client.Requests[1].Arguments["frameId"]!.GetValue<int>());
    }

    [Fact]
    public void ReadVariables_WhenStoppedEventHasNoThreadId_ReturnsUnavailableWithoutDapRequests()
    {
        var emit = EmitPlayerMove();
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");
        var session = CreateSession(emit);
        var stopped = session.ResolveStoppedProbe(branchProbe.ProbeId, synthetic: false);
        var client = new FakeDapRequestClient();
        var backend = new DapScriptFrameVariableBackend(client, emit.DebugMap);

        var snapshot = backend.ReadVariables(stopped);

        Assert.False(snapshot.Available);
        Assert.Contains("thread id", snapshot.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public void ReadPausedSnapshot_WithDapFrameBackend_ReturnsFrameScopes()
    {
        var emit = EmitPlayerMove();
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");
        var host = DebugScriptHost.Load(emit);
        host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        var session = CreateSession(emit);
        var stopped = session.ResolveStoppedProbe(branchProbe.ProbeId, synthetic: false, threadId: 11);
        var client = new FakeDapRequestClient();
        client.EnqueueResponse(new JsonObject
        {
            ["stackFrames"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 101,
                    ["name"] = "Update",
                    ["source"] = new JsonObject
                    {
                        ["path"] = emit.DebugMap.SourceDocumentPath
                    },
                    ["line"] = stopped.Line,
                    ["column"] = stopped.Column
                }
            }
        });
        client.EnqueueResponse(new JsonObject
        {
            ["scopes"] = new JsonArray
            {
                new JsonObject { ["name"] = "Arguments", ["variablesReference"] = 201 },
                new JsonObject { ["name"] = "Locals", ["variablesReference"] = 0 },
                new JsonObject { ["name"] = "This", ["variablesReference"] = 0 }
            }
        });
        client.EnqueueResponse(new JsonObject
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
        var backend = new DapScriptFrameVariableBackend(client, emit.DebugMap);

        var snapshot = session.ReadPausedSnapshot(host, stopped, entityId: 101, frameVariables: backend);

        var arguments = Assert.Single(snapshot.Scopes, scope => scope.Kind == ScriptDebugScopeKind.Arguments);
        Assert.True(arguments.Available);
        Assert.Equal("delta", Assert.Single(arguments.Variables).Name);
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

    private sealed record DapRequest(string Command, JsonObject Arguments);
}
