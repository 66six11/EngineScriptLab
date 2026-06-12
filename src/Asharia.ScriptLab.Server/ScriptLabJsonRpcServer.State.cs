using System.Text.Json.Nodes;

namespace ScriptLab;

public sealed partial class ScriptLabJsonRpcServer
{
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