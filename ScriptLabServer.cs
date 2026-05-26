using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ScriptLab;

public sealed record ScriptLabServerOptions(
    string DefaultOutputDirectory = "bin/ScriptDebug",
    string? DapAdapterPath = null,
    IReadOnlyList<string>? DapAdapterArguments = null);

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

public sealed record ScriptLabRegisterDebugHostParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    int ProcessId = 0,
    string? HostKind = "cppClr",
    bool TerminateOnDisconnect = false,
    string? BridgeManifestPath = null,
    string? EnginePackageRoot = null);

public sealed record ScriptLabAttachDebugHostParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    int ProcessId = 0,
    string? HostKind = "cppClr",
    bool? TerminateOnDisconnect = null,
    string? BridgeManifestPath = null,
    string? EnginePackageRoot = null);

public sealed record ScriptLabDisconnectDebugHostParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    bool? TerminateDebuggee = null);

public sealed record ScriptLabWaitDebugHostExitParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    int TimeoutMilliseconds = 0);

public sealed record ScriptLabDrainDebugEventsParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    int EntityId = 1,
    int TimeoutMilliseconds = 0,
    long? AfterEventSequence = null);

public sealed record ScriptLabReadVariablesParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    int? DebugStateId = null,
    string? ScopeKind = null,
    int? VariablesReference = null,
    int? Start = null,
    int? Count = null);

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
    int? DebugStateId,
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
    string? Granularity,
    DapDebugSessionLifecycle? Lifecycle);

public sealed record ScriptLabDebugHostRegistrationResult(
    string Status,
    string Backend,
    string Reason,
    ScriptLabRegisteredDebugHost? Host);

public sealed record ScriptLabRegisteredDebugHost(
    string HostKind,
    int ProcessId,
    bool TerminateOnDisconnect,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ProcessStartTimeUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BridgeManifestPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EnginePackageRoot = null);

public sealed record ScriptLabDebugHostAttachmentResult(
    string Status,
    string Backend,
    string HostKind,
    int ProcessId,
    bool TerminateOnDisconnect,
    string Reason,
    ScriptLabDapAttachTarget AttachTarget,
    IReadOnlyList<ScriptBreakpointBackendResult> BreakpointResults,
    DapDebugSessionLifecycle? Lifecycle);

public sealed record ScriptLabDapAttachTarget(
    string HostKind,
    int ProcessId,
    bool TerminateOnDisconnect,
    string AssemblyPath,
    string PdbPath,
    string DebugMapPath,
    string AssemblyMvid,
    string PdbId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? ProcessStartTimeUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BridgeManifestPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EnginePackageRoot = null);

public sealed record ScriptLabDebugHostDisconnectResult(
    string Status,
    string Backend,
    bool TerminateDebuggee,
    string Reason,
    DapDebugSessionLifecycle? Lifecycle);

public sealed record ScriptLabDebugHostExitWaitResult(
    string Status,
    string Backend,
    string Reason,
    int? ProcessId,
    int? ExitCode,
    DapDebugSessionLifecycle? Lifecycle);

public sealed record ScriptLabDrainDebugEventsResult(
    string Backend,
    IReadOnlyList<ScriptStoppedEvent> StoppedEvents,
    IReadOnlyList<DapLifecycleEvent> LifecycleEvents,
    IReadOnlyList<DapBreakpointEvent> BreakpointEvents,
    ScriptStoppedEvent? StoppedEvent,
    int? DebugStateId,
    ScriptPausedSnapshot? PausedSnapshot,
    DapDebugSessionLifecycle? Lifecycle,
    long? EventSequence = null,
    long NextEventSequence = 1,
    long? EarliestEventSequence = null);

public sealed record ScriptLabReadVariablesResult(
    string Status,
    string Backend,
    string Reason,
    int? DebugStateId,
    string? ScopeKind,
    int? VariablesReference,
    int Start,
    int Count,
    int TotalCount,
    IReadOnlyList<ScriptDebugVariable> Variables);

public sealed record ScriptLabDapAttachRequest(
    ScriptDebugSession Session,
    ScriptDebugMap DebugMap,
    string SourcePath,
    JsonObject InitializeArguments,
    JsonObject AttachArguments);

public sealed class ScriptLabJsonRpcServer : IDisposable
{
    public const string DapAdapterPathEnvironmentVariable = "SCRIPTLAB_DAP_ADAPTER";
    private static readonly IReadOnlyList<string> DefaultDapAdapterArguments = new[] { "--interpreter=vscode" };
    private const int MaxCachedDebugEventBatches = 64;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object syncRoot = new();
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

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (state is not null)
            {
                ClearAttachedHostState(state);
            }

            state = null;
            Monitor.PulseAll(syncRoot);
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
        if (string.Equals(method, "drainDebugEvents", StringComparison.Ordinal))
        {
            return DrainDebugEvents(ReadParams(parameters, new ScriptLabDrainDebugEventsParams()));
        }

        if (string.Equals(method, "waitDebugHostExit", StringComparison.Ordinal))
        {
            return WaitDebugHostExit(ReadParams(parameters, new ScriptLabWaitDebugHostExitParams()));
        }

        lock (syncRoot)
        {
            return DispatchLocked(method, parameters);
        }
    }

    private object? DispatchLocked(string method, JsonNode? parameters)
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
            "registerDebugHost" => RegisterDebugHost(
                ReadParams(parameters, new ScriptLabRegisterDebugHostParams())),
            "getRegisteredDebugHost" => GetRegisteredDebugHost(),
            "attachDebugHost" => AttachDebugHost(ReadParams(parameters, new ScriptLabAttachDebugHostParams())),
            "disconnectDebugHost" => DisconnectDebugHost(
                ReadParams(parameters, new ScriptLabDisconnectDebugHostParams())),
            "readVariables" => ReadVariables(ReadParams(parameters, new ScriptLabReadVariablesParams())),
            "continue" => ContinueDebug(ReadParams(parameters, new ScriptLabContinueDebugParams())),
            "step" => StepDebug(ReadParams(parameters, new ScriptLabStepDebugParams())),
            "getPausedSnapshot" => GetPausedSnapshot(),
            "getTraceSnapshot" => GetTraceSnapshot(),
            _ => throw new InvalidOperationException($"Unknown method '{method}'.")
        };
    }

    private ScriptLabLoadGraphResult LoadGraph(ScriptLabLoadGraphParams parameters)
    {
        var previousState = state;
        var scriptPath = ResolveScriptPath(parameters.ScriptPath);
        var outputDirectory = ResolveOutputDirectory(parameters.OutputDirectory);
        var module = BehaviorIrLowerer.LowerFile(scriptPath);
        var graph = BlueprintGraphProjector.Project(module);
        var emit = DebugScriptCompiler.EmitFile(scriptPath, outputDirectory);
        var sourceText = File.ReadAllText(emit.DebugMap.SourceDocumentPath);
        var session = new ScriptDebugSession(emit.DebugMap, sourceText, emit.DebugMap.SourceDocumentPath);
        var host = DebugScriptHost.Load(emit);
        var backend = new ProbeScriptBreakpointBackend(host);

        var nextState = new ServerState(
            scriptPath,
            outputDirectory,
            graph,
            emit,
            session,
            host,
            backend);
        if (previousState is not null)
        {
            ClearAttachedHostState(previousState);
        }

        state = nextState;
        Monitor.PulseAll(syncRoot);

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
        current.CurrentDebugStateId = stoppedEvent is null
            ? null
            : AssignDebugState(current);

        return new ScriptLabRunDebugResult(
            current.ScriptPath,
            current.OutputDirectory,
            current.Emit.DebugMap.BehaviorId,
            parameters.EntityId,
            current.CurrentDebugStateId,
            breakpoints,
            backendResults,
            probeEvents,
            ingest,
            stoppedEvent,
            pausedSnapshot);
    }

    private ScriptLabDebugHostRegistrationResult RegisterDebugHost(
        ScriptLabRegisterDebugHostParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        var host = CreateRegisteredDebugHost(parameters);
        current.RegisteredHost = host;

        return new ScriptLabDebugHostRegistrationResult(
            "registered",
            "dap",
            "C++ CLR host is registered for a later DAP attach.",
            host);
    }

    private ScriptLabDebugHostRegistrationResult GetRegisteredDebugHost()
    {
        var host = state?.RegisteredHost;
        return new ScriptLabDebugHostRegistrationResult(
            host is null ? "none" : "registered",
            "dap",
            host is null
                ? "No C++ CLR host is registered for the current script session."
                : "C++ CLR host is registered for the current script session.",
            host);
    }

    private ScriptLabDebugHostAttachmentResult AttachDebugHost(ScriptLabAttachDebugHostParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        var attachTarget = CreateDapAttachTarget(current, parameters);
        var attachArguments = CreateDapAttachArguments(attachTarget);
        ClearPausedState(current);
        current.AttachedHost = attachTarget;
        current.PendingAttachArguments = attachArguments;
        var attachResult = TryAttachDapRuntime(current, attachTarget, attachArguments);
        if (attachResult is not null)
        {
            StartDebugEventPump(current);
        }

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
            attachResult?.BreakpointResults ?? Array.Empty<ScriptBreakpointBackendResult>(),
            attachResult?.Lifecycle);
    }

    public static JsonObject CreateDapAttachArguments(ScriptLabDapAttachTarget attachTarget)
    {
        var arguments = new JsonObject
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

        if (!string.IsNullOrWhiteSpace(attachTarget.BridgeManifestPath))
        {
            arguments["bridgeManifestPath"] = attachTarget.BridgeManifestPath;
        }

        if (!string.IsNullOrWhiteSpace(attachTarget.EnginePackageRoot))
        {
            arguments["enginePackageRoot"] = attachTarget.EnginePackageRoot;
        }

        if (attachTarget.ProcessStartTimeUtc is not null)
        {
            arguments["processStartTimeUtc"] = attachTarget.ProcessStartTimeUtc;
        }

        return arguments;
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
            var lifecycle = current.DapRuntime.Lifecycle;
            ClearAttachedHostState(current);
            return new ScriptLabDebugHostDisconnectResult(
                "disconnected",
                "dap",
                terminateDebuggee,
                "DAP debug session was disconnected from the C++ CLR host.",
                lifecycle);
        }

        if (current.AttachedHost is not null)
        {
            ClearAttachedHostState(current);
            return new ScriptLabDebugHostDisconnectResult(
                "detached",
                "dap",
                terminateDebuggee,
                "Recorded C++ CLR host attach target was cleared; no DAP debug session was connected.",
                Lifecycle: null);
        }

        return new ScriptLabDebugHostDisconnectResult(
            "notAttached",
            "probe",
            terminateDebuggee,
            "No debug host is attached.",
            Lifecycle: null);
    }

    private ScriptLabDebugHostExitWaitResult WaitDebugHostExit(
        ScriptLabWaitDebugHostExitParams parameters)
    {
        int? processId;
        DateTimeOffset? processStartTimeUtc;
        int? cachedExitCode;
        DapDebugSessionLifecycle? lifecycle;
        lock (syncRoot)
        {
            var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
            processId = current.AttachedHost?.ProcessId;
            processStartTimeUtc = current.AttachedHost?.ProcessStartTimeUtc;
            lifecycle = current.DapRuntime?.Lifecycle;
            cachedExitCode = GetLatestDebugHostExitCode(current);
            if (processId is null)
            {
                return new ScriptLabDebugHostExitWaitResult(
                    "notAttached",
                    GetDebugBackend(current),
                    "No debug host is attached.",
                    ProcessId: null,
                    ExitCode: null,
                    lifecycle);
            }

            if (cachedExitCode is not null)
            {
                return new ScriptLabDebugHostExitWaitResult(
                    "exited",
                    "dap",
                    "The attached debug host process exit was observed through DAP lifecycle events.",
                    processId,
                    cachedExitCode,
                    lifecycle);
            }
        }

        var timeoutMilliseconds = Math.Max(0, parameters.TimeoutMilliseconds);
        try
        {
            using var process = Process.GetProcessById(processId.Value);
            if (processStartTimeUtc is not null &&
                !ProcessStartTimesMatch(process, processStartTimeUtc.Value))
            {
                return new ScriptLabDebugHostExitWaitResult(
                    "staleProcessId",
                    "dap",
                    "The attached debug host process id now belongs to a different process.",
                    processId,
                    ExitCode: null,
                    lifecycle);
            }

            if (!process.WaitForExit(timeoutMilliseconds))
            {
                return new ScriptLabDebugHostExitWaitResult(
                    "running",
                    "dap",
                    "The attached debug host process is still running.",
                    processId,
                    ExitCode: null,
                    lifecycle);
            }

            return new ScriptLabDebugHostExitWaitResult(
                "exited",
                "dap",
                "The attached debug host process exited.",
                processId,
                process.ExitCode,
                lifecycle);
        }
        catch (ArgumentException)
        {
            return new ScriptLabDebugHostExitWaitResult(
                "exited",
                "dap",
                "The attached debug host process id no longer exists.",
                processId,
                cachedExitCode,
                lifecycle);
        }
        catch (InvalidOperationException)
        {
            return new ScriptLabDebugHostExitWaitResult(
                "exited",
                "dap",
                "The attached debug host process id no longer exists.",
                processId,
                cachedExitCode,
                lifecycle);
        }
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
                    Granularity: null,
                    current.DapRuntime.Lifecycle);
            }

            return new ScriptLabExecutionControlResult(
                "unsupported",
                "dap",
                "The C++ CLR host attach target is recorded, but no DAP debug session is connected yet.",
                parameters.ThreadId,
                StepKind: null,
                Granularity: null,
                Lifecycle: null);
        }

        return new ScriptLabExecutionControlResult(
            "unsupported",
            "probe",
            "The probe debug backend cannot continue a suspended debugger thread.",
            parameters.ThreadId,
            StepKind: null,
            Granularity: null,
            Lifecycle: null);
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
                    parameters.Granularity,
                    current.DapRuntime.Lifecycle);
            }

            return new ScriptLabExecutionControlResult(
                "unsupported",
                "dap",
                "The C++ CLR host attach target is recorded, but no DAP debug session is connected yet.",
                parameters.ThreadId,
                parameters.Kind,
                parameters.Granularity,
                Lifecycle: null);
        }

        return new ScriptLabExecutionControlResult(
            "unsupported",
            "probe",
            "The probe debug backend cannot step a suspended debugger thread.",
            parameters.ThreadId,
            parameters.Kind,
            parameters.Granularity,
            Lifecycle: null);
    }

    private ScriptPausedSnapshot? GetPausedSnapshot()
    {
        var current = RequireState();
        current.Session.SetWatchObservationEnabled(true);
        current.Host.SetWatchEnabled(true);
        return current.LastPausedSnapshot;
    }

    private ScriptLabReadVariablesResult ReadVariables(ScriptLabReadVariablesParams parameters)
    {
        var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
        var backend = GetDebugBackend(current);
        if (parameters.DebugStateId is not null &&
            parameters.DebugStateId != current.CurrentDebugStateId)
        {
            return CreateReadVariablesResult(
                "stale",
                backend,
                "The requested debugStateId is no longer current.",
                current.CurrentDebugStateId,
                parameters.ScopeKind,
                parameters.VariablesReference,
                parameters,
                Array.Empty<ScriptDebugVariable>());
        }

        if (current.CurrentDebugStateId is null || current.LastPausedSnapshot is null)
        {
            return CreateReadVariablesResult(
                "unavailable",
                backend,
                "There is no current paused debug state.",
                current.CurrentDebugStateId,
                parameters.ScopeKind,
                parameters.VariablesReference,
                parameters,
                Array.Empty<ScriptDebugVariable>());
        }

        if (parameters.VariablesReference is not null)
        {
            if (parameters.VariablesReference.Value <= 0)
            {
                return CreateReadVariablesResult(
                    "ok",
                    backend,
                    "The variables reference has no child variables.",
                    current.CurrentDebugStateId,
                    parameters.ScopeKind,
                    parameters.VariablesReference,
                    parameters,
                    Array.Empty<ScriptDebugVariable>());
            }

            if (current.DapRuntime is null)
            {
                return CreateReadVariablesResult(
                    "unsupported",
                    backend,
                    "DAP variablesReference reads require an attached DAP debug session.",
                    current.CurrentDebugStateId,
                    parameters.ScopeKind,
                    parameters.VariablesReference,
                    parameters,
                    Array.Empty<ScriptDebugVariable>());
            }

            var variables = CreateDapVariableResults(
                current.DapRuntime.ReadVariables(parameters.VariablesReference.Value),
                parameters.VariablesReference.Value);
            return CreateReadVariablesResult(
                "ok",
                backend,
                "Read variables from the current DAP paused state.",
                current.CurrentDebugStateId,
                parameters.ScopeKind,
                parameters.VariablesReference,
                parameters,
                variables);
        }

        var snapshot = current.LastPausedSnapshot;
        if (!string.IsNullOrWhiteSpace(parameters.ScopeKind))
        {
            var scope = snapshot.Scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.Kind, parameters.ScopeKind, StringComparison.OrdinalIgnoreCase));
            if (scope is null)
            {
                return CreateReadVariablesResult(
                    "unavailable",
                    backend,
                    $"The current paused snapshot does not contain scope '{parameters.ScopeKind}'.",
                    current.CurrentDebugStateId,
                    parameters.ScopeKind,
                    parameters.VariablesReference,
                    parameters,
                    Array.Empty<ScriptDebugVariable>());
            }

            return CreateReadVariablesResult(
                scope.Available ? "ok" : "unavailable",
                backend,
                scope.Reason,
                current.CurrentDebugStateId,
                scope.Kind,
                parameters.VariablesReference,
                parameters,
                scope.Available ? scope.Variables : Array.Empty<ScriptDebugVariable>());
        }

        return CreateReadVariablesResult(
            "ok",
            backend,
            "Read variables from the current paused snapshot.",
            current.CurrentDebugStateId,
            null,
            null,
            parameters,
            snapshot.Scopes
                .Where(scope => scope.Available)
                .SelectMany(scope => scope.Variables)
                .ToArray());
    }

    private ScriptLabDrainDebugEventsResult DrainDebugEvents(ScriptLabDrainDebugEventsParams parameters)
    {
        var timeout = TimeSpan.FromMilliseconds(Math.Max(0, parameters.TimeoutMilliseconds));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        lock (syncRoot)
        {
            var current = EnsureState(parameters.ScriptPath, parameters.OutputDirectory);
            if (parameters.AfterEventSequence is < 0)
            {
                throw new InvalidOperationException("afterEventSequence must be zero or a positive event sequence.");
            }

            current.PreferredDebugEventEntityId = parameters.EntityId;
            var afterEventSequence = parameters.AfterEventSequence ?? current.DefaultDrainAfterEventSequence;
            while (true)
            {
                var cached = TryGetCachedDebugEvents(current, afterEventSequence);
                if (cached is not null)
                {
                    return MarkDefaultDrainCursorIfNeeded(
                        current,
                        PrepareDebugEventsForClient(current, cached, parameters.EntityId),
                        parameters.AfterEventSequence);
                }

                if (current.DapRuntime is null)
                {
                    return AddDebugEventCursor(
                        current,
                        new ScriptLabDrainDebugEventsResult(
                            current.AttachedHost is null ? "probe" : "dap",
                            Array.Empty<ScriptStoppedEvent>(),
                            Array.Empty<DapLifecycleEvent>(),
                            Array.Empty<DapBreakpointEvent>(),
                            StoppedEvent: null,
                            DebugStateId: current.CurrentDebugStateId,
                            PausedSnapshot: null,
                            Lifecycle: null));
                }

                var result = DrainDebugEventsOnce(current, parameters.EntityId);
                if (HasDebugEvents(result))
                {
                    var sequenced = AddDebugEventCursor(current, result);
                    return MarkDefaultDrainCursorIfNeeded(
                        current,
                        PrepareDebugEventsForClient(current, sequenced, parameters.EntityId),
                        parameters.AfterEventSequence);
                }

                if (current.DebugEventPumpException is not null)
                {
                    throw new InvalidOperationException(
                        $"DAP debug event pump failed: {current.DebugEventPumpException.Message}");
                }

                if (stopwatch.Elapsed >= timeout)
                {
                    return AddDebugEventCursor(
                        current,
                        new ScriptLabDrainDebugEventsResult(
                            "dap",
                            Array.Empty<ScriptStoppedEvent>(),
                            Array.Empty<DapLifecycleEvent>(),
                            Array.Empty<DapBreakpointEvent>(),
                            StoppedEvent: null,
                            DebugStateId: current.CurrentDebugStateId,
                            PausedSnapshot: null,
                            Lifecycle: current.DapRuntime.Lifecycle));
                }

                var remaining = timeout - stopwatch.Elapsed;
                var wait = remaining <= TimeSpan.Zero || remaining > TimeSpan.FromMilliseconds(10)
                    ? TimeSpan.FromMilliseconds(10)
                    : remaining;
                Monitor.Wait(syncRoot, wait);
            }
        }
    }

    private static bool HasDebugEvents(ScriptLabDrainDebugEventsResult result)
    {
        return result.StoppedEvents.Count > 0 ||
               result.LifecycleEvents.Count > 0 ||
               result.BreakpointEvents.Count > 0;
    }

    private static ScriptLabDrainDebugEventsResult? TryGetCachedDebugEvents(
        ServerState current,
        long? afterEventSequence)
    {
        if (afterEventSequence is null)
        {
            return null;
        }

        var after = afterEventSequence.Value;
        var cached = current.DebugEventBatches.FirstOrDefault(batch =>
            batch.EventSequence is long sequence && sequence > after);
        return cached is null
            ? null
            : cached with
            {
                NextEventSequence = current.NextDebugEventSequence,
                EarliestEventSequence = GetEarliestDebugEventSequence(current)
            };
    }

    private ScriptLabDrainDebugEventsResult AddDebugEventCursor(
        ServerState current,
        ScriptLabDrainDebugEventsResult result)
    {
        if (!HasDebugEvents(result))
        {
            return result with
            {
                EventSequence = null,
                NextEventSequence = current.NextDebugEventSequence,
                EarliestEventSequence = GetEarliestDebugEventSequence(current)
            };
        }

        var sequence = current.NextDebugEventSequence++;
        var sequenced = result with
        {
            EventSequence = sequence,
            NextEventSequence = current.NextDebugEventSequence
        };
        current.DebugEventBatches.Add(sequenced);
        if (current.DebugEventBatches.Count > MaxCachedDebugEventBatches)
        {
            current.DebugEventBatches.RemoveAt(0);
        }

        Monitor.PulseAll(syncRoot);

        return sequenced with
        {
            EarliestEventSequence = GetEarliestDebugEventSequence(current)
        };
    }

    private static ScriptLabDrainDebugEventsResult MarkDefaultDrainCursorIfNeeded(
        ServerState current,
        ScriptLabDrainDebugEventsResult result,
        long? requestedAfterEventSequence)
    {
        if (requestedAfterEventSequence is null && result.EventSequence is long sequence)
        {
            current.DefaultDrainAfterEventSequence = sequence;
        }

        return result;
    }

    private static ScriptLabDrainDebugEventsResult PrepareDebugEventsForClient(
        ServerState current,
        ScriptLabDrainDebugEventsResult result,
        int entityId)
    {
        if (result.PausedSnapshot is not null ||
            result.StoppedEvent is null ||
            result.DebugStateId is null ||
            current.DapRuntime is null ||
            current.CurrentDebugStateId != result.DebugStateId ||
            !current.DapRuntime.IsCurrentStoppedEvent(result.StoppedEvent))
        {
            return result;
        }

        var pausedSnapshot = ReadDapPausedSnapshot(current, result.StoppedEvent, entityId);
        current.LastEntityId = entityId;
        current.LastStoppedEvent = result.StoppedEvent;
        current.LastPausedSnapshot = pausedSnapshot;

        var hydrated = result with
        {
            PausedSnapshot = pausedSnapshot
        };
        if (hydrated.EventSequence is long sequence)
        {
            UpdateCachedPausedSnapshot(current, sequence, pausedSnapshot);
        }

        return hydrated;
    }

    private static void UpdateCachedPausedSnapshot(
        ServerState current,
        long eventSequence,
        ScriptPausedSnapshot pausedSnapshot)
    {
        for (var index = 0; index < current.DebugEventBatches.Count; index++)
        {
            if (current.DebugEventBatches[index].EventSequence == eventSequence)
            {
                current.DebugEventBatches[index] = current.DebugEventBatches[index] with
                {
                    PausedSnapshot = pausedSnapshot
                };
                return;
            }
        }
    }

    private static long? GetEarliestDebugEventSequence(ServerState current)
    {
        return current.DebugEventBatches.Count == 0
            ? null
            : current.DebugEventBatches[0].EventSequence;
    }

    private static int? GetLatestDebugHostExitCode(ServerState current)
    {
        for (var index = current.DebugEventBatches.Count - 1; index >= 0; index--)
        {
            var exitCode = current.DebugEventBatches[index]
                .LifecycleEvents
                .LastOrDefault(lifecycleEvent =>
                    string.Equals(lifecycleEvent.Kind, DapLifecycleEventKind.Exited, StringComparison.Ordinal))
                ?.ExitCode;
            if (exitCode is not null)
            {
                return exitCode;
            }
        }

        return null;
    }

    private static ScriptLabReadVariablesResult CreateReadVariablesResult(
        string status,
        string backend,
        string reason,
        int? debugStateId,
        string? scopeKind,
        int? variablesReference,
        ScriptLabReadVariablesParams parameters,
        IReadOnlyList<ScriptDebugVariable> variables)
    {
        var totalCount = variables.Count;
        var start = Math.Max(0, parameters.Start ?? 0);
        if (start > totalCount)
        {
            start = totalCount;
        }

        var count = parameters.Count is null
            ? totalCount - start
            : Math.Max(0, Math.Min(parameters.Count.Value, totalCount - start));
        var page = variables
            .Skip(start)
            .Take(count)
            .ToArray();

        return new ScriptLabReadVariablesResult(
            status,
            backend,
            reason,
            debugStateId,
            scopeKind,
            variablesReference,
            start,
            page.Length,
            totalCount,
            page);
    }

    private static IReadOnlyList<ScriptDebugVariable> CreateDapVariableResults(
        IReadOnlyList<DapVariable> variables,
        int parentVariablesReference)
    {
        return variables
            .Select(variable => new ScriptDebugVariable(
                variable.Name,
                $"dap:variables:{parentVariablesReference}:{variable.Name}",
                variable.Type,
                variable.Value,
                RawValue: null,
                BehaviorId: null,
                EntityId: null,
                FieldId: null,
                Accessibility: null,
                Serialization: null,
                Writable: false)
            {
                VariablesReference = variable.VariablesReference > 0
                    ? variable.VariablesReference
                    : null
            })
            .ToArray();
    }

    private static ScriptLabDrainDebugEventsResult DrainDebugEventsOnce(
        ServerState current,
        int entityId)
    {
        var drain = current.DapRuntime!.DrainDebugEvents();
        var stoppedEvent = drain.StoppedEvents.LastOrDefault();
        var debugStateId = drain.CurrentStoppedEvent is null
            ? current.CurrentDebugStateId
            : AssignDebugState(current);

        current.LastEntityId = drain.CurrentStoppedEvent is null ? current.LastEntityId : entityId;
        current.LastStoppedEvent = stoppedEvent ?? current.LastStoppedEvent;
        if (drain.LifecycleEvents.Count > 0 && drain.CurrentStoppedEvent is null)
        {
            current.LastPausedSnapshot = null;
            current.CurrentDebugStateId = null;
            debugStateId = null;
        }
        else if (drain.CurrentStoppedEvent is not null)
        {
            current.LastPausedSnapshot = null;
        }

        return new ScriptLabDrainDebugEventsResult(
            "dap",
            drain.StoppedEvents,
            drain.LifecycleEvents,
            drain.BreakpointEvents,
            stoppedEvent,
            debugStateId,
            PausedSnapshot: null,
            Lifecycle: drain.Lifecycle);
    }

    private static ScriptPausedSnapshot ReadDapPausedSnapshot(
        ServerState current,
        ScriptStoppedEvent stoppedEvent,
        int entityId)
    {
        current.Host.MountBehavior(entityId, current.Emit.DebugMap.BehaviorId);
        return current.DapRuntime!.ReadPausedSnapshot(current.Host, stoppedEvent, entityId);
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
        current.CurrentDebugStateId = null;
    }

    private static int AssignDebugState(ServerState current)
    {
        var debugStateId = current.NextDebugStateId++;
        current.CurrentDebugStateId = debugStateId;
        return debugStateId;
    }

    private static string GetDebugBackend(ServerState current)
    {
        return current.DapRuntime is not null || current.AttachedHost is not null
            ? "dap"
            : "probe";
    }

    private void StartDebugEventPump(ServerState current)
    {
        StopDebugEventPump(current);
        var cancellation = new CancellationTokenSource();
        current.DebugEventPumpCancellation = cancellation;
        current.DebugEventPumpException = null;
        current.DebugEventPumpTask = Task.Run(() => RunDebugEventPump(current, cancellation.Token));
    }

    private void RunDebugEventPump(ServerState current, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lock (syncRoot)
                {
                    if (state != current || current.DapRuntime is null)
                    {
                        return;
                    }

                    var result = DrainDebugEventsOnce(current, current.PreferredDebugEventEntityId);
                    if (HasDebugEvents(result))
                    {
                        AddDebugEventCursor(current, result);
                    }
                }

                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(10));
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            lock (syncRoot)
            {
                if (state == current)
                {
                    current.DebugEventPumpException = exception;
                    Monitor.PulseAll(syncRoot);
                }
            }
        }
    }

    private static void StopDebugEventPump(ServerState current)
    {
        current.DebugEventPumpCancellation?.Cancel();
        current.DebugEventPumpCancellation = null;
        current.DebugEventPumpTask = null;
        current.DebugEventPumpException = null;
    }

    private void ClearAttachedHostState(ServerState current)
    {
        StopDebugEventPump(current);
        current.DapAdapterProcess?.Dispose();
        current.AttachedHost = null;
        current.PendingAttachArguments = null;
        current.DapRuntime = null;
        current.DapAdapterProcess = null;
        Monitor.PulseAll(syncRoot);
    }

    private static ScriptLabDapAttachTarget CreateDapAttachTarget(
        ServerState current,
        ScriptLabAttachDebugHostParams parameters)
    {
        var registeredHost = current.RegisteredHost;
        var hostKind = ResolveCppClrHostKind(parameters.HostKind ?? registeredHost?.HostKind);
        var processId = parameters.ProcessId > 0
            ? parameters.ProcessId
            : registeredHost?.ProcessId ?? 0;
        if (processId <= 0)
        {
            throw new InvalidOperationException(
                "processId must be a positive C++ CLR host process id, or registerDebugHost must be called first.");
        }

        return new ScriptLabDapAttachTarget(
            hostKind,
            processId,
            parameters.TerminateOnDisconnect ?? registeredHost?.TerminateOnDisconnect ?? false,
            current.Emit.AssemblyPath,
            current.Emit.PdbPath,
            current.Emit.DebugMapPath,
            current.Emit.DebugMap.AssemblyMvid,
            current.Emit.DebugMap.PdbId,
            registeredHost?.ProcessStartTimeUtc ?? TryGetProcessStartTimeUtc(processId),
            NormalizeOptionalPath(parameters.BridgeManifestPath) ?? registeredHost?.BridgeManifestPath,
            NormalizeOptionalPath(parameters.EnginePackageRoot) ?? registeredHost?.EnginePackageRoot);
    }

    private static ScriptLabRegisteredDebugHost CreateRegisteredDebugHost(
        ScriptLabRegisterDebugHostParams parameters)
    {
        if (parameters.ProcessId <= 0)
        {
            throw new InvalidOperationException("processId must be a positive C++ CLR host process id.");
        }

        return new ScriptLabRegisteredDebugHost(
            ResolveCppClrHostKind(parameters.HostKind),
            parameters.ProcessId,
            parameters.TerminateOnDisconnect,
            TryGetProcessStartTimeUtc(parameters.ProcessId),
            NormalizeOptionalPath(parameters.BridgeManifestPath),
            NormalizeOptionalPath(parameters.EnginePackageRoot));
    }

    private static DateTimeOffset? TryGetProcessStartTimeUtc(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return ToUtcOffset(process.StartTime);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool ProcessStartTimesMatch(Process process, DateTimeOffset expectedStartTimeUtc)
    {
        try
        {
            return ToUtcOffset(process.StartTime) == expectedStartTimeUtc;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    private static DateTimeOffset ToUtcOffset(DateTime startTime)
    {
        var localStartTime = startTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(startTime, DateTimeKind.Local)
            : startTime;
        return new DateTimeOffset(localStartTime).ToUniversalTime();
    }

    private static string ResolveCppClrHostKind(string? hostKind)
    {
        var resolved = string.IsNullOrWhiteSpace(hostKind) ? "cppClr" : hostKind;
        if (!string.Equals(resolved, "cppClr", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("hostKind must be 'cppClr' for C++ direct CLR hosting.");
        }

        return resolved;
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private DapDebugSessionLaunchResult? TryAttachDapRuntime(
        ServerState current,
        ScriptLabDapAttachTarget attachTarget,
        JsonObject attachArguments)
    {
        if (dapAttachFactory is null)
        {
            return TryAttachConfiguredDapRuntime(current, attachTarget);
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

    private DapDebugSessionLaunchResult? TryAttachConfiguredDapRuntime(
        ServerState current,
        ScriptLabDapAttachTarget attachTarget)
    {
        if (string.IsNullOrWhiteSpace(options.DapAdapterPath))
        {
            return null;
        }

        DapAdapterProcess? adapter = null;
        try
        {
            adapter = DapAdapterProcess.Start(
                options.DapAdapterPath,
                options.DapAdapterArguments ?? DefaultDapAdapterArguments,
                current.OutputDirectory);
            var client = new DapDebugSessionClient(adapter.Client, adapter.Client);
            var launcher = new DapDebugSessionLauncher(client);
            var attachResult = launcher.Attach(
                current.Session,
                current.Emit.DebugMap,
                current.Emit.DebugMap.SourceDocumentPath,
                CreateDapInitializeArguments(),
                CreateDapAdapterAttachArguments(attachTarget));

            current.DapAdapterProcess = adapter;
            adapter = null;
            current.DapRuntime = attachResult.Runtime;
            current.LastBackendResults = attachResult.BreakpointResults;
            return attachResult;
        }
        finally
        {
            adapter?.Dispose();
        }
    }

    private static JsonObject CreateDapAdapterAttachArguments(ScriptLabDapAttachTarget attachTarget)
    {
        return new JsonObject
        {
            ["processId"] = attachTarget.ProcessId
        };
    }

    private static JsonObject CreateDapInitializeArguments()
    {
        return new JsonObject
        {
            ["adapterID"] = "scriptlab",
            ["clientID"] = "scriptlab-jsonrpc",
            ["clientName"] = "ScriptLab JSON-RPC",
            ["pathFormat"] = "path",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["supportsVariableType"] = true,
            ["supportsRunInTerminalRequest"] = false
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

        public int? CurrentDebugStateId { get; set; }

        public int NextDebugStateId { get; set; } = 1;

        public long NextDebugEventSequence { get; set; } = 1;

        public long DefaultDrainAfterEventSequence { get; set; }

        public int PreferredDebugEventEntityId { get; set; } = 1;

        public List<ScriptLabDrainDebugEventsResult> DebugEventBatches { get; } = new();

        public ScriptLabRegisteredDebugHost? RegisteredHost { get; set; }

        public ScriptLabDapAttachTarget? AttachedHost { get; set; }

        public JsonObject? PendingAttachArguments { get; set; }

        public DapDebugSessionRuntime? DapRuntime { get; set; }

        public DapAdapterProcess? DapAdapterProcess { get; set; }

        public CancellationTokenSource? DebugEventPumpCancellation { get; set; }

        public Task? DebugEventPumpTask { get; set; }

        public Exception? DebugEventPumpException { get; set; }
    }
}
