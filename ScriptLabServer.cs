using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScriptLab;

public sealed record ScriptLabServerOptions(string DefaultOutputDirectory = "bin/ScriptDebug");

public sealed record ScriptLabLoadGraphParams(
    string? ScriptPath = null,
    string? OutputDirectory = null);

public sealed record ScriptLabSetBlueprintBreakpointsParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    IReadOnlyList<string>? GraphNodeIds = null);

public sealed record ScriptLabSetSourceBreakpointsParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    string? SourcePath = null,
    IReadOnlyList<ScriptSourceBreakpointRequest>? Breakpoints = null);

public sealed record ScriptLabResolveBlueprintBreakpointParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    string? GraphNodeId = null);

public sealed record ScriptLabResolveSourceBreakpointParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    string? SourcePath = null,
    int Line = 1,
    int Column = 1);

public sealed record ScriptLabGetBreakpointsParams(
    string? ScriptPath = null,
    string? OutputDirectory = null);

public sealed record ScriptLabRunDebugParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    string? SourcePath = null,
    IReadOnlyList<ScriptSourceBreakpointRequest>? SourceBreakpoints = null,
    string? GraphNodeId = null,
    IReadOnlyList<string>? GraphNodeIds = null,
    int EntityId = 1,
    float Delta = 0.016f,
    bool PressKeyW = true,
    bool ObserveTrace = false,
    bool ObserveWatch = false);

public sealed record ScriptLabContinueDebugParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    int? ThreadId = null);

public sealed record ScriptLabStepDebugParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    int? ThreadId = null,
    string Kind = "next",
    string? Granularity = null);

public sealed record ScriptLabAttachDebugHostParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    int ProcessId = 0,
    string HostKind = "cppClr",
    bool TerminateOnDisconnect = false);

public sealed record ScriptLabDisconnectDebugHostParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    bool? TerminateDebuggee = null);

public sealed record ScriptLabLoadGraphResult(
    string ScriptPath,
    string OutputDirectory,
    string BehaviorId,
    BlueprintGraphModule Graph,
    ScriptDebugMap DebugMap);

public sealed record ScriptLabSetBreakpointsResult(
    IReadOnlyList<ScriptBreakpointState> Breakpoints,
    IReadOnlyList<ScriptBreakpointBackendResult> BackendResults);

public sealed record ScriptLabGetBreakpointsResult(
    IReadOnlyList<ScriptBreakpointState> Breakpoints,
    IReadOnlyList<ScriptBreakpointBackendResult> BackendResults);

public sealed record ScriptLabRunDebugResult(
    string ScriptPath,
    string OutputDirectory,
    string BehaviorId,
    int EntityId,
    IReadOnlyList<ScriptBreakpointState> Breakpoints,
    IReadOnlyList<ScriptBreakpointBackendResult> BackendResults,
    IReadOnlyList<DebugRuntimeProbeEvent> ProbeEvents,
    ScriptProbeEventIngestResult Ingest,
    ScriptStoppedEvent? StoppedEvent,
    ScriptPausedSnapshot? PausedSnapshot);

public sealed record ScriptLabExecutionControlResult(
    string Status,
    string Backend,
    string Reason,
    int? ThreadId,
    string? StepKind,
    string? Granularity);

public sealed record ScriptLabDebugHostAttachmentResult(
    string Status,
    string Backend,
    string HostKind,
    int ProcessId,
    bool TerminateOnDisconnect,
    string Reason,
    ScriptLabDapAttachTarget AttachTarget,
    IReadOnlyList<ScriptBreakpointBackendResult> BreakpointResults);

public sealed record ScriptLabDapAttachTarget(
    string HostKind,
    int ProcessId,
    bool TerminateOnDisconnect,
    string AssemblyPath,
    string PdbPath,
    string DebugMapPath,
    string AssemblyMvid,
    string PdbId);

public sealed record ScriptLabDebugHostDisconnectResult(
    string Status,
    string Backend,
    bool TerminateDebuggee,
    string Reason);

public sealed record ScriptLabDapAttachRequest(
    ScriptDebugSession Session,
    ScriptDebugMap DebugMap,
    string SourcePath,
    JsonObject InitializeArguments,
    JsonObject AttachArguments);

public sealed class ScriptLabJsonRpcServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ScriptLabServerOptions options;
    private readonly Func<ScriptLabDapAttachRequest, DapDebugSessionLaunchResult>? dapAttachFactory;
    private ServerState? state;

    public ScriptLabJsonRpcServer(
        ScriptLabServerOptions? options = null,
        Func<ScriptLabDapAttachRequest, DapDebugSessionLaunchResult>? dapAttachFactory = null)
    {
        this.options = options ?? new ScriptLabServerOptions();
        this.dapAttachFactory = dapAttachFactory;
    }

    public async Task RunAsync(
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync();
            if (line is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await output.WriteLineAsync(HandleRequest(line));
            await output.FlushAsync();
        }
    }

    public string HandleRequest(string requestJson)
    {
        JsonNode? id = null;
        try
        {
            var request = JsonNode.Parse(requestJson)?.AsObject()
                ?? throw new InvalidOperationException("Request must be a JSON object.");
            id = request["id"]?.DeepClone();
            var method = request["method"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(method))
            {
                return CreateError(id, -32600, "Request method is required.");
            }

            var result = Dispatch(method, request["params"]);
            return CreateResult(id, result);
        }
        catch (JsonException exception)
        {
            return CreateError(id, -32700, exception.Message);
        }
        catch (Exception exception)
        {
            return CreateError(id, -32000, exception.Message);
        }
    }

    private object? Dispatch(string method, JsonNode? parameters)
    {
        return method switch
        {
            "loadGraph" => LoadGraph(ReadParams(parameters, new ScriptLabLoadGraphParams())),
            "resolveBlueprintBreakpoint" => ResolveBlueprintBreakpoint(
                ReadParams(parameters, new ScriptLabResolveBlueprintBreakpointParams())),
            "resolveSourceBreakpoint" => ResolveSourceBreakpoint(
                ReadParams(parameters, new ScriptLabResolveSourceBreakpointParams())),
            "setBlueprintBreakpoints" => SetBlueprintBreakpoints(
                ReadParams(parameters, new ScriptLabSetBlueprintBreakpointsParams())),
            "setSourceBreakpoints" => SetSourceBreakpoints(
                ReadParams(parameters, new ScriptLabSetSourceBreakpointsParams())),
            "getBreakpoints" => GetBreakpoints(
                ReadParams(parameters, new ScriptLabGetBreakpointsParams())),
            "runDebug" => RunDebug(ReadParams(parameters, new ScriptLabRunDebugParams())),
            "attachDebugHost" => AttachDebugHost(ReadParams(parameters, new ScriptLabAttachDebugHostParams())),
            "disconnectDebugHost" => DisconnectDebugHost(
                ReadParams(parameters, new ScriptLabDisconnectDebugHostParams())),
            "continue" => ContinueDebug(ReadParams(parameters, new ScriptLabContinueDebugParams())),
            "step" => StepDebug(ReadParams(parameters, new ScriptLabStepDebugParams())),
            "getPausedSnapshot" => GetPausedSnapshot(),
            "getTraceSnapshot" => GetTraceSnapshot(),
            _ => throw new InvalidOperationException($"Unknown method '{method}'.")
        };
    }

    private ScriptLabLoadGraphResult LoadGraph(ScriptLabLoadGraphParams parameters)
    {
        var scriptPath = ResolveScriptPath(parameters.ScriptPath);
        var outputDirectory = ResolveOutputDirectory(parameters.OutputDirectory);
        var module = BehaviorIrLowerer.LowerFile(scriptPath);
        var graph = BlueprintGraphProjector.Project(module);
        var emit = DebugScriptCompiler.EmitFile(scriptPath, outputDirectory);
        var sourceText = File.ReadAllText(emit.DebugMap.SourceDocumentPath);
        var session = new ScriptDebugSession(emit.DebugMap, sourceText, emit.DebugMap.SourceDocumentPath);
        var host = DebugScriptHost.Load(emit);
        var backend = new ProbeScriptBreakpointBackend(host);

        state = new ServerState(
            scriptPath,
            outputDirectory,
            graph,
            emit,
            session,
            host,
            backend);

        return new ScriptLabLoadGraphResult(
            scriptPath,
            outputDirectory,
            emit.DebugMap.BehaviorId,
            graph,
            emit.DebugMap);
    }

    private ScriptLabSetBreakpointsResult SetBlueprintBreakpoints(
        ScriptLabSetBlueprintBreakpointsParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        var graphNodeIds = parameters.GraphNodeIds ?? Array.Empty<string>();
        var breakpoints = current.Session.SetBlueprintBreakpoints(graphNodeIds);
        var backendResults = current.Session.ApplySourceBreakpoints(
            current.Backend,
            current.Emit.DebugMap.SourceDocumentPath);
        current.LastBackendResults = backendResults;
        return new ScriptLabSetBreakpointsResult(breakpoints, backendResults);
    }

    private ScriptLabSetBreakpointsResult SetSourceBreakpoints(
        ScriptLabSetSourceBreakpointsParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        var sourcePath = ResolveSourcePath(parameters.SourcePath, current);
        var requests = parameters.Breakpoints ?? Array.Empty<ScriptSourceBreakpointRequest>();
        var breakpoints = current.Session.SetSourceBreakpoints(sourcePath, requests);
        var backendResults = current.Session.ApplySourceBreakpoints(current.Backend, sourcePath);
        current.LastBackendResults = backendResults;
        return new ScriptLabSetBreakpointsResult(breakpoints, backendResults);
    }

    private ScriptBreakpointBinding ResolveBlueprintBreakpoint(
        ScriptLabResolveBlueprintBreakpointParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        if (string.IsNullOrWhiteSpace(parameters.GraphNodeId))
        {
            throw new InvalidOperationException("graphNodeId is required.");
        }

        return current.Session.ResolveBlueprintBreakpoint(parameters.GraphNodeId);
    }

    private ScriptBreakpointBinding ResolveSourceBreakpoint(
        ScriptLabResolveSourceBreakpointParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        return current.Session.ResolveSourceBreakpoint(
            ResolveSourcePath(parameters.SourcePath, current),
            parameters.Line,
            parameters.Column);
    }

    private ScriptLabGetBreakpointsResult GetBreakpoints(ScriptLabGetBreakpointsParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        return new ScriptLabGetBreakpointsResult(current.Session.Breakpoints, current.LastBackendResults);
    }

    private ScriptLabRunDebugResult RunDebug(ScriptLabRunDebugParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        var hasExplicitSourceBreakpoints = parameters.SourceBreakpoints is not null;
        var requestedGraphNodeIds = GetRequestedGraphNodeIds(
            parameters,
            current.Emit.DebugMap,
            hasExplicitSourceBreakpoints);
        IReadOnlyList<ScriptBreakpointState> breakpoints = current.Session.Breakpoints;
        IReadOnlyList<ScriptBreakpointBackendResult> backendResults = current.LastBackendResults;

        if (hasExplicitSourceBreakpoints)
        {
            breakpoints = current.Session.SetSourceBreakpoints(
                ResolveSourcePath(parameters.SourcePath, current),
                parameters.SourceBreakpoints ?? Array.Empty<ScriptSourceBreakpointRequest>());
        }

        if (requestedGraphNodeIds.Count > 0)
        {
            breakpoints = current.Session.SetBlueprintBreakpoints(requestedGraphNodeIds);
        }

        if (hasExplicitSourceBreakpoints || requestedGraphNodeIds.Count > 0)
        {
            backendResults = current.Session.ApplySourceBreakpoints(
                current.Backend,
                current.Emit.DebugMap.SourceDocumentPath);
            current.LastBackendResults = backendResults;
        }

        var instance = current.Host.MountBehavior(parameters.EntityId, current.Emit.DebugMap.BehaviorId);
        current.Host.ClearInput();
        if (parameters.PressKeyW)
        {
            current.Host.SetInputKeyDown("W", isDown: true);
        }

        var observeTrace = parameters.ObserveTrace || current.Session.TraceObservationEnabled;
        var observeWatch = parameters.ObserveWatch || current.Session.WatchObservationEnabled;
        current.Session.SetTraceObservationEnabled(observeTrace);
        current.Session.SetWatchObservationEnabled(observeWatch);
        current.Host.SetTraceEnabled(observeTrace);
        current.Host.SetWatchEnabled(observeWatch);
        current.Session.ResetProbeEventCursor();
        current.Session.ClearWatchValues();
        current.Session.ClearTrace();
        current.Host.ClearProbeEvents();
        instance.InvokeUpdate(parameters.Delta);

        var probeEvents = current.Host.GetProbeEvents();
        var ingest = current.Session.IngestProbeEvents(probeEvents);
        var stoppedEvent = ingest.StoppedEvents.LastOrDefault();
        var pausedSnapshot = stoppedEvent is null
            ? null
            : current.Session.ReadPausedSnapshot(current.Host, stoppedEvent, parameters.EntityId);

        current.LastEntityId = parameters.EntityId;
        current.LastIngest = ingest;
        current.LastStoppedEvent = stoppedEvent;
        current.LastPausedSnapshot = pausedSnapshot;

        return new ScriptLabRunDebugResult(
            current.ScriptPath,
            current.OutputDirectory,
            current.Emit.DebugMap.BehaviorId,
            parameters.EntityId,
            breakpoints,
            backendResults,
            probeEvents,
            ingest,
            stoppedEvent,
            pausedSnapshot);
    }

    private ScriptLabDebugHostAttachmentResult AttachDebugHost(ScriptLabAttachDebugHostParams parameters)
    {
        if (parameters.ProcessId <= 0)
        {
            throw new InvalidOperationException("processId must be a positive C++ CLR host process id.");
        }

        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        var attachTarget = CreateDapAttachTarget(current, parameters);
        var attachArguments = CreateDapAttachArguments(attachTarget);
        ClearPausedState(current);
        current.AttachedHost = attachTarget;
        current.PendingAttachArguments = attachArguments;
        var attachResult = TryAttachDapRuntime(current, attachArguments);

        return new ScriptLabDebugHostAttachmentResult(
            attachResult is null ? "unsupported" : "attached",
            "dap",
            attachTarget.HostKind,
            attachTarget.ProcessId,
            attachTarget.TerminateOnDisconnect,
            attachResult is null
                ? "C++ CLR host attach requires the DAP backend path, which is not connected yet."
                : "C++ CLR host attach target is connected through the DAP backend.",
            attachTarget,
            attachResult?.BreakpointResults ?? Array.Empty<ScriptBreakpointBackendResult>());
    }

    public static JsonObject CreateDapAttachArguments(ScriptLabDapAttachTarget attachTarget)
    {
        return new JsonObject
        {
            ["processId"] = attachTarget.ProcessId,
            ["hostKind"] = attachTarget.HostKind,
            ["terminateOnDisconnect"] = attachTarget.TerminateOnDisconnect,
            ["assemblyPath"] = attachTarget.AssemblyPath,
            ["pdbPath"] = attachTarget.PdbPath,
            ["debugMapPath"] = attachTarget.DebugMapPath,
            ["assemblyMvid"] = attachTarget.AssemblyMvid,
            ["pdbId"] = attachTarget.PdbId
        };
    }

    private ScriptLabDebugHostDisconnectResult DisconnectDebugHost(
        ScriptLabDisconnectDebugHostParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        var terminateDebuggee = parameters.TerminateDebuggee ??
                                current.AttachedHost?.TerminateOnDisconnect ??
                                false;
        ClearPausedState(current);

        if (current.DapRuntime is not null)
        {
            current.DapRuntime.Disconnect(terminateDebuggee);
            ClearAttachedHostState(current);
            return new ScriptLabDebugHostDisconnectResult(
                "disconnected",
                "dap",
                terminateDebuggee,
                "DAP debug session was disconnected from the C++ CLR host.");
        }

        if (current.AttachedHost is not null)
        {
            ClearAttachedHostState(current);
            return new ScriptLabDebugHostDisconnectResult(
                "detached",
                "dap",
                terminateDebuggee,
                "Recorded C++ CLR host attach target was cleared; no DAP debug session was connected.");
        }

        return new ScriptLabDebugHostDisconnectResult(
            "notAttached",
            "probe",
            terminateDebuggee,
            "No debug host is attached.");
    }

    private ScriptLabExecutionControlResult ContinueDebug(ScriptLabContinueDebugParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        ClearPausedState(current);
        if (current.AttachedHost is not null)
        {
            if (current.DapRuntime is not null && parameters.ThreadId is not null)
            {
                var continueResult = current.DapRuntime.Continue(parameters.ThreadId.Value);
                return new ScriptLabExecutionControlResult(
                    continueResult.AllThreadsContinued ? "continued" : "continuedThread",
                    "dap",
                    "DAP continue request was sent to the attached C++ CLR host.",
                    parameters.ThreadId,
                    StepKind: null,
                    Granularity: null);
            }

            return new ScriptLabExecutionControlResult(
                "unsupported",
                "dap",
                "The C++ CLR host attach target is recorded, but no DAP debug session is connected yet.",
                parameters.ThreadId,
                StepKind: null,
                Granularity: null);
        }

        return new ScriptLabExecutionControlResult(
            "unsupported",
            "probe",
            "The probe debug backend cannot continue a suspended debugger thread.",
            parameters.ThreadId,
            StepKind: null,
            Granularity: null);
    }

    private ScriptLabExecutionControlResult StepDebug(ScriptLabStepDebugParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        ClearPausedState(current);
        if (current.AttachedHost is not null)
        {
            if (current.DapRuntime is not null && parameters.ThreadId is not null)
            {
                current.DapRuntime.Next(parameters.ThreadId.Value, parameters.Granularity);
                return new ScriptLabExecutionControlResult(
                    "stepped",
                    "dap",
                    "DAP step request was sent to the attached C++ CLR host.",
                    parameters.ThreadId,
                    parameters.Kind,
                    parameters.Granularity);
            }

            return new ScriptLabExecutionControlResult(
                "unsupported",
                "dap",
                "The C++ CLR host attach target is recorded, but no DAP debug session is connected yet.",
                parameters.ThreadId,
                parameters.Kind,
                parameters.Granularity);
        }

        return new ScriptLabExecutionControlResult(
            "unsupported",
            "probe",
            "The probe debug backend cannot step a suspended debugger thread.",
            parameters.ThreadId,
            parameters.Kind,
            parameters.Granularity);
    }

    private ScriptPausedSnapshot? GetPausedSnapshot()
    {
        var current = RequireState();
        current.Session.SetWatchObservationEnabled(true);
        current.Host.SetWatchEnabled(true);
        return current.LastPausedSnapshot;
    }

    private ScriptTraceSnapshot GetTraceSnapshot()
    {
        var current = RequireState();
        current.Session.SetTraceObservationEnabled(true);
        current.Host.SetTraceEnabled(true);
        return current.Session.GetTraceSnapshot();
    }

    private ServerState RequireState()
    {
        return state ?? throw new InvalidOperationException(
            "No script is loaded. Call loadGraph or runDebug with scriptPath first.");
    }

    private ServerState EnsureState(string? scriptPath, string? outputDirectory)
    {
        if (scriptPath is not null || state is null)
        {
            LoadGraph(new ScriptLabLoadGraphParams(scriptPath, outputDirectory));
        }

        return RequireState();
    }

    private IReadOnlyList<string> GetRequestedGraphNodeIds(
        ScriptLabRunDebugParams parameters,
        ScriptDebugMap debugMap,
        bool hasExplicitSourceBreakpoints)
    {
        if (parameters.GraphNodeIds is { Count: > 0 })
        {
            return parameters.GraphNodeIds;
        }

        if (!string.IsNullOrWhiteSpace(parameters.GraphNodeId))
        {
            return new[] { parameters.GraphNodeId };
        }

        if (hasExplicitSourceBreakpoints)
        {
            return Array.Empty<string>();
        }

        if (state?.Session.Breakpoints.Count > 0)
        {
            return Array.Empty<string>();
        }

        var defaultGraphNodeId = FindDefaultGraphNodeId(debugMap);
        return defaultGraphNodeId is null
            ? Array.Empty<string>()
            : new[] { defaultGraphNodeId };
    }

    private static string? FindDefaultGraphNodeId(ScriptDebugMap debugMap)
    {
        var sites = debugMap.Functions.SelectMany(function => function.Sites).ToArray();
        return sites.FirstOrDefault(site =>
                site.BreakableVerified &&
                site.ProbeId is not null)
            ?.GraphNodeId
            ?? sites.FirstOrDefault(site => site.ProbeId is not null)?.GraphNodeId;
    }

    private string ResolveScriptPath(string? scriptPath)
    {
        return ScriptPathResolver.Resolve(
            string.IsNullOrWhiteSpace(scriptPath)
                ? Path.Combine("Samples", "PlayerMove.ash.cs")
                : scriptPath);
    }

    private static void ClearPausedState(ServerState current)
    {
        current.LastIngest = null;
        current.LastStoppedEvent = null;
        current.LastPausedSnapshot = null;
    }

    private static void ClearAttachedHostState(ServerState current)
    {
        current.AttachedHost = null;
        current.PendingAttachArguments = null;
        current.DapRuntime = null;
    }

    private static ScriptLabDapAttachTarget CreateDapAttachTarget(
        ServerState current,
        ScriptLabAttachDebugHostParams parameters)
    {
        if (!string.Equals(parameters.HostKind, "cppClr", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("hostKind must be 'cppClr' for C++ direct CLR hosting.");
        }

        return new ScriptLabDapAttachTarget(
            parameters.HostKind,
            parameters.ProcessId,
            parameters.TerminateOnDisconnect,
            current.Emit.AssemblyPath,
            current.Emit.PdbPath,
            current.Emit.DebugMapPath,
            current.Emit.DebugMap.AssemblyMvid,
            current.Emit.DebugMap.PdbId);
    }

    private DapDebugSessionLaunchResult? TryAttachDapRuntime(
        ServerState current,
        JsonObject attachArguments)
    {
        if (dapAttachFactory is null)
        {
            return null;
        }

        var attachResult = dapAttachFactory(new ScriptLabDapAttachRequest(
            current.Session,
            current.Emit.DebugMap,
            current.Emit.DebugMap.SourceDocumentPath,
            CreateDapInitializeArguments(),
            attachArguments.DeepClone().AsObject()));
        current.DapRuntime = attachResult.Runtime;
        current.LastBackendResults = attachResult.BreakpointResults;
        return attachResult;
    }

    private static JsonObject CreateDapInitializeArguments()
    {
        return new JsonObject
        {
            ["adapterID"] = "scriptlab",
            ["clientID"] = "scriptlab-jsonrpc",
            ["clientName"] = "ScriptLab JSON-RPC"
        };
    }

    private string ResolveOutputDirectory(string? outputDirectory)
    {
        return Path.GetFullPath(
            string.IsNullOrWhiteSpace(outputDirectory)
                ? options.DefaultOutputDirectory
                : outputDirectory);
    }

    private static string ResolveSourcePath(string? sourcePath, ServerState current)
    {
        return Path.GetFullPath(
            string.IsNullOrWhiteSpace(sourcePath)
                ? current.Emit.DebugMap.SourceDocumentPath
                : sourcePath);
    }

    private static T ReadParams<T>(JsonNode? parameters, T defaultValue)
    {
        return parameters is null
            ? defaultValue
            : parameters.Deserialize<T>(JsonOptions) ?? defaultValue;
    }

    private static string CreateResult(JsonNode? id, object? result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = JsonSerializer.SerializeToNode(result, JsonOptions)
        };
        return response.ToJsonString(JsonOptions);
    }

    private static string CreateError(JsonNode? id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return response.ToJsonString(JsonOptions);
    }

    private sealed class ServerState
    {
        public ServerState(
            string scriptPath,
            string outputDirectory,
            BlueprintGraphModule graph,
            DebugScriptEmitResult emit,
            ScriptDebugSession session,
            DebugScriptHost host,
            ProbeScriptBreakpointBackend backend)
        {
            ScriptPath = scriptPath;
            OutputDirectory = outputDirectory;
            Graph = graph;
            Emit = emit;
            Session = session;
            Host = host;
            Backend = backend;
        }

        public string ScriptPath { get; }

        public string OutputDirectory { get; }

        public BlueprintGraphModule Graph { get; }

        public DebugScriptEmitResult Emit { get; }

        public ScriptDebugSession Session { get; }

        public DebugScriptHost Host { get; }

        public ProbeScriptBreakpointBackend Backend { get; }

        public IReadOnlyList<ScriptBreakpointBackendResult> LastBackendResults { get; set; } =
            Array.Empty<ScriptBreakpointBackendResult>();

        public int? LastEntityId { get; set; }

        public ScriptProbeEventIngestResult? LastIngest { get; set; }

        public ScriptStoppedEvent? LastStoppedEvent { get; set; }

        public ScriptPausedSnapshot? LastPausedSnapshot { get; set; }

        public ScriptLabDapAttachTarget? AttachedHost { get; set; }

        public JsonObject? PendingAttachArguments { get; set; }

        public DapDebugSessionRuntime? DapRuntime { get; set; }
    }
}
