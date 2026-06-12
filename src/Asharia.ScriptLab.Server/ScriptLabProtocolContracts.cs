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

public sealed record ScriptLabPrepareDebugSessionParams(
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

public sealed record ScriptLabValidateDebugHostParams(
    string? ScriptPath = null,
    string? OutputDirectory = null,
    string? HostKind = "cppClr",
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ScriptDebugMap? DebugMap = null);

public sealed record ScriptLabPrepareDebugSessionResult(
    string ScriptPath,
    string OutputDirectory,
    string BehaviorId,
    ScriptDebugMap DebugMap,
    string AssemblyPath,
    string PdbPath,
    string DebugMapPath,
    string InstrumentedSourcePath,
    string ProbeManifestPath);

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

public sealed record ScriptLabDebugHostValidationResult(
    string Status,
    string Backend,
    string HostKind,
    string Reason,
    string BridgeManifestPath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? EnginePackageRoot,
    string GeneratedAssemblyPath,
    string PdbPath,
    string DebugMapPath,
    string SourceDocumentPath);

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
