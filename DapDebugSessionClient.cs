using System.Text.Json.Nodes;

namespace ScriptLab;

public interface IDapRequestClient
{
    JsonObject SendRequest(string command, JsonObject arguments);
}

public interface IDapEventSource
{
    IReadOnlyList<JsonObject> DrainEvents();
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

public sealed class DapDebugSessionClient
{
    private readonly IDapRequestClient client;
    private readonly IDapEventSource? eventSource;

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
        var initializedEventReceived = DrainRawEvents()
            .Any(message =>
                string.Equals(GetString(message, "type"), "event", StringComparison.Ordinal) &&
                string.Equals(GetString(message, "event"), "initialized", StringComparison.Ordinal));
        return new DapInitializedHandshake(
            DapBreakpointBackendCapabilities.FromInitializeResponseBody(responseBody),
            initializedEventReceived);
    }

    public void ConfigurationDone()
    {
        client.SendRequest("configurationDone", new JsonObject());
    }

    public void Launch(JsonObject arguments)
    {
        client.SendRequest("launch", arguments.DeepClone().AsObject());
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

    public IReadOnlyList<DapStoppedEvent> DrainStoppedEvents()
    {
        return DrainRawEvents()
            .Select(message => TryParseStoppedEvent(message, out var stoppedEvent) ? stoppedEvent : null)
            .Where(stoppedEvent => stoppedEvent is not null)
            .Cast<DapStoppedEvent>()
            .ToArray();
    }

    private IReadOnlyList<JsonObject> DrainRawEvents()
    {
        return eventSource?.DrainEvents() ?? Array.Empty<JsonObject>();
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

    private static string? GetString(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node)
            ? node?.GetValue<string>()
            : null;
    }
}
