using System.Diagnostics;
using System.Text.Json.Nodes;

namespace ScriptLab;

public interface IDapRequestClient
{
    JsonObject SendRequest(string command, JsonObject arguments);
}

public interface IDapPendingRequestClient
{
    DapPendingRequest SendRequestPending(string command, JsonObject arguments);
}

public interface IDapEventSource
{
    IReadOnlyList<JsonObject> DrainEvents();
}

public sealed class DapPendingRequest
{
    private readonly Lazy<JsonObject> response;

    public DapPendingRequest(Func<JsonObject> wait)
    {
        response = new Lazy<JsonObject>(wait);
    }

    public JsonObject Wait()
    {
        return response.Value;
    }
}

public sealed record DapSourceBreakpointRequest(
    int Line,
    int Column,
    string? Condition,
    string? HitCondition);

public sealed record DapBreakpointResponse(
    bool Verified,
    string? Message,
    int? Line,
    int? Column);

public sealed record DapStackFrame(
    int Id,
    string Name,
    string? SourcePath,
    int Line,
    int Column);

public sealed record DapScope(
    string Name,
    int VariablesReference);

public sealed record DapVariable(
    string Name,
    string Value,
    string? Type,
    int VariablesReference);

public sealed record DapStoppedEvent(
    string Reason,
    int? ThreadId,
    bool AllThreadsStopped,
    string? Description);

public sealed record DapInitializedHandshake(
    DapBreakpointBackendCapabilities Capabilities,
    bool InitializedEventReceived);

public sealed record DapContinueResult(bool AllThreadsContinued);

public static class DapLifecycleEventKind
{
    public const string Terminated = "terminated";
    public const string Exited = "exited";
}

public sealed record DapLifecycleEvent(
    string Kind,
    int? ExitCode,
    bool Restart);

public sealed record DapBreakpointEvent(
    string Reason,
    bool Verified,
    string? Message,
    int? Line,
    int? Column,
    string? SourcePath);

public sealed record DapDebugEvent(
    DapStoppedEvent? StoppedEvent,
    DapLifecycleEvent? LifecycleEvent,
    DapBreakpointEvent? BreakpointEvent);

public sealed record DapDebugEventDrain(
    IReadOnlyList<DapDebugEvent> Events,
    IReadOnlyList<DapStoppedEvent> StoppedEvents,
    IReadOnlyList<DapLifecycleEvent> LifecycleEvents,
    IReadOnlyList<DapBreakpointEvent> BreakpointEvents);

public sealed class DapDebugSessionClient
{
    private readonly IDapRequestClient client;
    private readonly IDapEventSource? eventSource;
    private readonly Queue<JsonObject> bufferedEvents = new();

    public DapDebugSessionClient(IDapRequestClient client)
        : this(client, client as IDapEventSource)
    {
    }

    public DapDebugSessionClient(IDapRequestClient client, IDapEventSource? eventSource)
    {
        this.client = client;
        this.eventSource = eventSource;
    }

    public IReadOnlyList<DapBreakpointResponse> SetBreakpoints(
        string sourcePath,
        IReadOnlyList<DapSourceBreakpointRequest> breakpoints)
    {
        var requestBreakpoints = new JsonArray();
        foreach (var breakpoint in breakpoints)
        {
            requestBreakpoints.Add(CreateDapSourceBreakpoint(breakpoint));
        }

        var responseBody = client.SendRequest(
            "setBreakpoints",
            new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["path"] = Path.GetFullPath(sourcePath)
                },
                ["breakpoints"] = requestBreakpoints,
                ["sourceModified"] = false
            });

        return GetArray(responseBody, "breakpoints")
            .Select(node =>
            {
                var breakpoint = node!.AsObject();
                return new DapBreakpointResponse(
                    GetBoolean(breakpoint, "verified"),
                    GetString(breakpoint, "message"),
                    GetInt32(breakpoint, "line"),
                    GetInt32(breakpoint, "column"));
            })
            .ToArray();
    }

    public DapInitializedHandshake Initialize(JsonObject? arguments = null)
    {
        var responseBody = client.SendRequest(
            "initialize",
            arguments?.DeepClone().AsObject() ?? new JsonObject());
        var initializedEventReceived = DrainInitializedEvent();
        return new DapInitializedHandshake(
            DapBreakpointBackendCapabilities.FromInitializeResponseBody(responseBody),
            initializedEventReceived);
    }

    public bool WaitForInitializedEvent(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (DrainInitializedEvent())
            {
                return true;
            }

            if (stopwatch.Elapsed >= timeout)
            {
                return false;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(10));
        }
    }

    public void ConfigurationDone()
    {
        client.SendRequest("configurationDone", new JsonObject());
    }

    public void Launch(JsonObject arguments)
    {
        BeginLaunch(arguments).Wait();
    }

    public DapPendingRequest BeginLaunch(JsonObject arguments)
    {
        return SendRequestPending("launch", arguments.DeepClone().AsObject());
    }

    public void Attach(JsonObject arguments)
    {
        BeginAttach(arguments).Wait();
    }

    public DapPendingRequest BeginAttach(JsonObject arguments)
    {
        return SendRequestPending("attach", arguments.DeepClone().AsObject());
    }

    public DapContinueResult Continue(int threadId)
    {
        var responseBody = client.SendRequest(
            "continue",
            new JsonObject
            {
                ["threadId"] = threadId
            });
        return new DapContinueResult(
            GetBoolean(responseBody, "allThreadsContinued", defaultValue: true));
    }

    public void Next(int threadId, string? granularity = null)
    {
        var arguments = new JsonObject
        {
            ["threadId"] = threadId
        };

        if (!string.IsNullOrWhiteSpace(granularity))
        {
            arguments["granularity"] = granularity;
        }

        client.SendRequest("next", arguments);
    }

    public void Disconnect(bool terminateDebuggee)
    {
        client.SendRequest(
            "disconnect",
            new JsonObject
            {
                ["terminateDebuggee"] = terminateDebuggee
            });
    }

    public void Terminate(bool restart = false)
    {
        client.SendRequest(
            "terminate",
            new JsonObject
            {
                ["restart"] = restart
            });
    }

    public IReadOnlyList<DapStackFrame> StackTrace(int threadId)
    {
        var responseBody = client.SendRequest(
            "stackTrace",
            new JsonObject
            {
                ["threadId"] = threadId
            });

        return GetArray(responseBody, "stackFrames")
            .Select(node =>
            {
                var frame = node!.AsObject();
                return new DapStackFrame(
                    GetRequiredInt32(frame, "id"),
                    GetString(frame, "name") ?? string.Empty,
                    GetSourcePath(frame),
                    GetRequiredInt32(frame, "line"),
                    GetRequiredInt32(frame, "column"));
            })
            .ToArray();
    }

    public IReadOnlyList<DapScope> Scopes(int frameId)
    {
        var responseBody = client.SendRequest(
            "scopes",
            new JsonObject
            {
                ["frameId"] = frameId
            });

        return GetArray(responseBody, "scopes")
            .Select(node =>
            {
                var scope = node!.AsObject();
                return new DapScope(
                    GetString(scope, "name") ?? string.Empty,
                    GetRequiredInt32(scope, "variablesReference"));
            })
            .ToArray();
    }

    public IReadOnlyList<DapVariable> Variables(int variablesReference)
    {
        var responseBody = client.SendRequest(
            "variables",
            new JsonObject
            {
                ["variablesReference"] = variablesReference
            });

        return GetArray(responseBody, "variables")
            .Select(node =>
            {
                var variable = node!.AsObject();
                return new DapVariable(
                    GetString(variable, "name") ?? string.Empty,
                    GetString(variable, "value") ?? string.Empty,
                    GetString(variable, "type"),
                    GetInt32(variable, "variablesReference") ?? 0);
            })
            .ToArray();
    }

    public static bool TryParseStoppedEvent(JsonObject message, out DapStoppedEvent stoppedEvent)
    {
        stoppedEvent = null!;

        if (!string.Equals(GetString(message, "type"), "event", StringComparison.Ordinal) ||
            !string.Equals(GetString(message, "event"), "stopped", StringComparison.Ordinal) ||
            !message.TryGetPropertyValue("body", out var bodyNode) ||
            bodyNode is not JsonObject body)
        {
            return false;
        }

        stoppedEvent = new DapStoppedEvent(
            GetString(body, "reason") ?? ScriptStoppedReason.Unknown,
            GetInt32(body, "threadId"),
            GetBoolean(body, "allThreadsStopped"),
            GetString(body, "description") ?? GetString(body, "text"));
        return true;
    }

    public static bool TryParseLifecycleEvent(JsonObject message, out DapLifecycleEvent lifecycleEvent)
    {
        lifecycleEvent = null!;

        if (!string.Equals(GetString(message, "type"), "event", StringComparison.Ordinal))
        {
            return false;
        }

        var eventName = GetString(message, "event");
        if (string.Equals(eventName, DapLifecycleEventKind.Terminated, StringComparison.Ordinal))
        {
            var body = GetBody(message);
            lifecycleEvent = new DapLifecycleEvent(
                DapLifecycleEventKind.Terminated,
                ExitCode: null,
                Restart: body is not null && GetBoolean(body, "restart"));
            return true;
        }

        if (string.Equals(eventName, DapLifecycleEventKind.Exited, StringComparison.Ordinal))
        {
            var body = GetBody(message);
            lifecycleEvent = new DapLifecycleEvent(
                DapLifecycleEventKind.Exited,
                body is null ? null : GetInt32(body, "exitCode"),
                Restart: false);
            return true;
        }

        return false;
    }

    public static bool TryParseBreakpointEvent(JsonObject message, out DapBreakpointEvent breakpointEvent)
    {
        breakpointEvent = null!;

        if (!string.Equals(GetString(message, "type"), "event", StringComparison.Ordinal) ||
            !string.Equals(GetString(message, "event"), "breakpoint", StringComparison.Ordinal) ||
            !message.TryGetPropertyValue("body", out var bodyNode) ||
            bodyNode is not JsonObject body ||
            !body.TryGetPropertyValue("breakpoint", out var breakpointNode) ||
            breakpointNode is not JsonObject breakpoint)
        {
            return false;
        }

        breakpointEvent = new DapBreakpointEvent(
            GetString(body, "reason") ?? string.Empty,
            GetBoolean(breakpoint, "verified"),
            GetString(breakpoint, "message"),
            GetInt32(breakpoint, "line"),
            GetInt32(breakpoint, "column"),
            GetSourcePath(breakpoint));
        return true;
    }

    public IReadOnlyList<DapStoppedEvent> DrainStoppedEvents()
    {
        return DrainDebugEvents().StoppedEvents;
    }

    public IReadOnlyList<DapLifecycleEvent> DrainLifecycleEvents()
    {
        return DrainDebugEvents().LifecycleEvents;
    }

    public DapDebugEventDrain DrainDebugEvents()
    {
        var events = DrainRawEvents()
            .Select(ParseDebugEvent)
            .Where(debugEvent => debugEvent is not null)
            .Cast<DapDebugEvent>()
            .ToArray();

        return new DapDebugEventDrain(
            events,
            events
                .Where(debugEvent => debugEvent.StoppedEvent is not null)
                .Select(debugEvent => debugEvent.StoppedEvent!)
                .ToArray(),
            events
                .Where(debugEvent => debugEvent.LifecycleEvent is not null)
                .Select(debugEvent => debugEvent.LifecycleEvent!)
                .ToArray(),
            events
                .Where(debugEvent => debugEvent.BreakpointEvent is not null)
                .Select(debugEvent => debugEvent.BreakpointEvent!)
                .ToArray());
    }

    private IReadOnlyList<JsonObject> DrainRawEvents()
    {
        var events = new List<JsonObject>();
        while (bufferedEvents.Count > 0)
        {
            events.Add(bufferedEvents.Dequeue());
        }

        if (eventSource is not null)
        {
            events.AddRange(eventSource.DrainEvents());
        }

        return events;
    }

    private bool DrainInitializedEvent()
    {
        var initializedEventReceived = false;
        foreach (var message in DrainRawEvents())
        {
            if (IsInitializedEvent(message))
            {
                initializedEventReceived = true;
                continue;
            }

            bufferedEvents.Enqueue(message);
        }

        return initializedEventReceived;
    }

    private DapPendingRequest SendRequestPending(string command, JsonObject arguments)
    {
        if (client is IDapPendingRequestClient pendingClient)
        {
            return pendingClient.SendRequestPending(command, arguments);
        }

        var requestTask = Task.Run(() => client.SendRequest(command, arguments));
        return new DapPendingRequest(() => requestTask.GetAwaiter().GetResult());
    }

    private static DapDebugEvent? ParseDebugEvent(JsonObject message)
    {
        if (TryParseStoppedEvent(message, out var stoppedEvent))
        {
            return new DapDebugEvent(stoppedEvent, LifecycleEvent: null, BreakpointEvent: null);
        }

        if (TryParseLifecycleEvent(message, out var lifecycleEvent))
        {
            return new DapDebugEvent(StoppedEvent: null, lifecycleEvent, BreakpointEvent: null);
        }

        if (TryParseBreakpointEvent(message, out var breakpointEvent))
        {
            return new DapDebugEvent(StoppedEvent: null, LifecycleEvent: null, breakpointEvent);
        }

        return null;
    }

    private static bool IsInitializedEvent(JsonObject message)
    {
        return string.Equals(GetString(message, "type"), "event", StringComparison.Ordinal) &&
               string.Equals(GetString(message, "event"), "initialized", StringComparison.Ordinal);
    }

    private static JsonObject CreateDapSourceBreakpoint(DapSourceBreakpointRequest breakpoint)
    {
        var sourceBreakpoint = new JsonObject
        {
            ["line"] = breakpoint.Line,
            ["column"] = breakpoint.Column
        };

        if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
        {
            sourceBreakpoint["condition"] = breakpoint.Condition;
        }

        if (!string.IsNullOrWhiteSpace(breakpoint.HitCondition))
        {
            sourceBreakpoint["hitCondition"] = breakpoint.HitCondition;
        }

        return sourceBreakpoint;
    }

    private static string? GetSourcePath(JsonObject frame)
    {
        return frame.TryGetPropertyValue("source", out var sourceNode) &&
               sourceNode is JsonObject source
            ? GetString(source, "path")
            : null;
    }

    private static JsonObject? GetBody(JsonObject message)
    {
        return message.TryGetPropertyValue("body", out var bodyNode) &&
               bodyNode is JsonObject body
            ? body
            : null;
    }

    private static IReadOnlyList<JsonNode?> GetArray(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node) &&
               node is JsonArray array
            ? array.ToArray()
            : Array.Empty<JsonNode?>();
    }

    private static int GetRequiredInt32(JsonObject json, string name)
    {
        return GetInt32(json, name)
            ?? throw new DapProtocolException($"DAP response is missing required integer field '{name}'.");
    }

    private static int? GetInt32(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node) && node is not null
            ? node.GetValue<int>()
            : null;
    }

    private static bool GetBoolean(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node) &&
               node is not null &&
               node.GetValue<bool>();
    }

    private static bool GetBoolean(JsonObject json, string name, bool defaultValue)
    {
        return json.TryGetPropertyValue(name, out var node) && node is not null
            ? node.GetValue<bool>()
            : defaultValue;
    }

    private static string? GetString(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node)
            ? node?.GetValue<string>()
            : null;
    }
}
