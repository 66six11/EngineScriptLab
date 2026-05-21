using ScriptLab;
using System.Text.Json.Nodes;

namespace ScriptLab.Tests;

public sealed class DapDebugSessionClientTests
{
    [Fact]
    public void StackTraceScopesAndVariables_SendExpectedDapRequests()
    {
        var requestClient = new FakeDapRequestClient();
        requestClient.EnqueueResponse(new JsonObject
        {
            ["stackFrames"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = 7,
                    ["name"] = "Update",
                    ["source"] = new JsonObject
                    {
                        ["path"] = @"C:\Project\PlayerMove.ash.cs"
                    },
                    ["line"] = 12,
                    ["column"] = 9
                }
            }
        });
        requestClient.EnqueueResponse(new JsonObject
        {
            ["scopes"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Locals",
                    ["variablesReference"] = 30
                }
            }
        });
        requestClient.EnqueueResponse(new JsonObject
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
        var client = new DapDebugSessionClient(requestClient);

        var frame = Assert.Single(client.StackTrace(threadId: 3));
        var scope = Assert.Single(client.Scopes(frame.Id));
        var variable = Assert.Single(client.Variables(scope.VariablesReference));

        Assert.Equal(7, frame.Id);
        Assert.Equal("Update", frame.Name);
        Assert.Equal(@"C:\Project\PlayerMove.ash.cs", frame.SourcePath);
        Assert.Equal(12, frame.Line);
        Assert.Equal(9, frame.Column);
        Assert.Equal("Locals", scope.Name);
        Assert.Equal(30, scope.VariablesReference);
        Assert.Equal("amount", variable.Name);
        Assert.Equal("0.128", variable.Value);
        Assert.Equal("float", variable.Type);

        Assert.Equal(new[] { "stackTrace", "scopes", "variables" }, requestClient.Requests.Select(request => request.Command));
        Assert.Equal(3, requestClient.Requests[0].Arguments["threadId"]!.GetValue<int>());
        Assert.Equal(7, requestClient.Requests[1].Arguments["frameId"]!.GetValue<int>());
        Assert.Equal(30, requestClient.Requests[2].Arguments["variablesReference"]!.GetValue<int>());
    }

    [Fact]
    public void SetBreakpoints_UsesSharedDapShape()
    {
        var requestClient = new FakeDapRequestClient();
        requestClient.EnqueueResponse(new JsonObject
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
        var client = new DapDebugSessionClient(requestClient);

        var response = Assert.Single(client.SetBreakpoints(
            "PlayerMove.ash.cs",
            new[]
            {
                new DapSourceBreakpointRequest(12, 9, "Speed > 0", "3")
            }));

        Assert.True(response.Verified);
        var request = Assert.Single(requestClient.Requests);
        Assert.Equal("setBreakpoints", request.Command);
        var breakpoint = Assert.Single(request.Arguments["breakpoints"]!.AsArray())!.AsObject();
        Assert.Equal(12, breakpoint["line"]!.GetValue<int>());
        Assert.Equal(9, breakpoint["column"]!.GetValue<int>());
        Assert.Equal("Speed > 0", breakpoint["condition"]!.GetValue<string>());
        Assert.Equal("3", breakpoint["hitCondition"]!.GetValue<string>());
    }

    [Fact]
    public void TryParseStoppedEvent_WhenEventIsStopped_ReturnsStoppedEvent()
    {
        var message = new JsonObject
        {
            ["type"] = "event",
            ["event"] = "stopped",
            ["body"] = new JsonObject
            {
                ["reason"] = "breakpoint",
                ["threadId"] = 11,
                ["allThreadsStopped"] = true,
                ["description"] = "Paused on breakpoint."
            }
        };

        var parsed = DapDebugSessionClient.TryParseStoppedEvent(message, out var stoppedEvent);

        Assert.True(parsed);
        Assert.Equal(ScriptStoppedReason.Breakpoint, stoppedEvent.Reason);
        Assert.Equal(11, stoppedEvent.ThreadId);
        Assert.True(stoppedEvent.AllThreadsStopped);
        Assert.Equal("Paused on breakpoint.", stoppedEvent.Description);
    }

    [Fact]
    public void TryParseStoppedEvent_WhenEventIsNotStopped_ReturnsFalse()
    {
        var message = new JsonObject
        {
            ["type"] = "event",
            ["event"] = "continued"
        };

        Assert.False(DapDebugSessionClient.TryParseStoppedEvent(message, out _));
    }

    [Fact]
    public void DrainStoppedEvents_WhenEventSourceHasMixedEvents_ReturnsStoppedEventsOnly()
    {
        var transport = new FakeDapTransport();
        transport.Events.Add(new JsonObject
        {
            ["type"] = "event",
            ["event"] = "initialized"
        });
        transport.Events.Add(new JsonObject
        {
            ["type"] = "event",
            ["event"] = "stopped",
            ["body"] = new JsonObject
            {
                ["reason"] = "step",
                ["threadId"] = 7,
                ["allThreadsStopped"] = false
            }
        });
        var client = new DapDebugSessionClient(transport, transport);

        var stoppedEvent = Assert.Single(client.DrainStoppedEvents());

        Assert.Equal(ScriptStoppedReason.Step, stoppedEvent.Reason);
        Assert.Equal(7, stoppedEvent.ThreadId);
        Assert.False(stoppedEvent.AllThreadsStopped);
        Assert.Empty(transport.Events);
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
        public List<JsonObject> Events { get; } = new();

        public JsonObject SendRequest(string command, JsonObject arguments)
        {
            throw new InvalidOperationException("Request path is not used by this test.");
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
