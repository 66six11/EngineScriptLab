using System.Text.Json.Nodes;

namespace ScriptLab;

public sealed partial class ScriptLabJsonRpcServer
{
    private sealed class ServerState
    {
        public ServerState(
            string scriptPath,
            string outputDirectory,
            string behaviorId,
            BlueprintGraphModule graph)
        {
            ScriptPath = scriptPath;
            OutputDirectory = outputDirectory;
            BehaviorId = behaviorId;
            Graph = graph;
        }

        public string ScriptPath { get; }

        public string OutputDirectory { get; }

        public string BehaviorId { get; }

        public BlueprintGraphModule Graph { get; }

        public DebugSessionState? Debug { get; private set; }

        public bool HasDebugSession => Debug is not null;

        public DebugScriptEmitResult Emit =>
            Debug?.Emit ?? throw new InvalidOperationException("No debug session is prepared. Call prepareDebugSession first.");

        public DebugSessionCore Session =>
            Debug?.Session ?? throw new InvalidOperationException("No debug session is prepared. Call prepareDebugSession first.");

        public DotnetDebugHost Host =>
            Debug?.Host ?? throw new InvalidOperationException("No debug session is prepared. Call prepareDebugSession first.");

        public ProbeScriptBreakpointBackend Backend =>
            Debug?.Backend ?? throw new InvalidOperationException("No debug session is prepared. Call prepareDebugSession first.");

        public void SetDebugSession(DebugSessionState debug)
        {
            Debug = debug;
        }

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

        public DapDebugBackendSession? DapBackend { get; set; }

        public CancellationTokenSource? DebugEventPumpCancellation { get; set; }

        public Task? DebugEventPumpTask { get; set; }

        public Exception? DebugEventPumpException { get; set; }
    }

    private sealed record DebugSessionState(
        DebugScriptEmitResult Emit,
        DebugSessionCore Session,
        DotnetDebugHost Host,
        ProbeScriptBreakpointBackend Backend);
}
