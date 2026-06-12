using System.Text.Json.Nodes;

namespace ScriptLab;

public static class DapDebugSessionPhase
{
    public const string Created = "created";
    public const string Initialized = "initialized";
    public const string BreakpointsConfigured = "breakpointsConfigured";
    public const string ConfigurationDone = "configurationDone";
    public const string Launched = "launched";
    public const string Attached = "attached";
    public const string Running = "running";
    public const string Stopped = "stopped";
    public const string Terminated = "terminated";
    public const string Exited = "exited";
    public const string Disconnected = "disconnected";
}

public sealed record DapDebugSessionLifecycle(
    string Phase,
    IReadOnlyList<string> CompletedPhases);

public sealed record DapDebugSessionLaunchResult(
    DapDebugSessionRuntime Runtime,
    DapBreakpointBackendCapabilities Capabilities,
    bool InitializedEventReceived,
    IReadOnlyList<ScriptBreakpointBackendResult> BreakpointResults,
    DapDebugSessionLifecycle Lifecycle);

public sealed class DapDebugSessionLauncher
{
    private static readonly TimeSpan DefaultInitializedEventTimeout = TimeSpan.FromSeconds(5);
    private readonly DapDebugSessionClient client;
    private readonly TimeSpan initializedEventTimeout;

    public DapDebugSessionLauncher(
        DapDebugSessionClient client,
        TimeSpan? initializedEventTimeout = null)
    {
        this.client = client;
        this.initializedEventTimeout = initializedEventTimeout ?? DefaultInitializedEventTimeout;
    }

    public DapDebugSessionLaunchResult Launch(
        DebugSessionCore session,
        ScriptDebugMap debugMap,
        string sourcePath,
        JsonObject? initializeArguments,
        JsonObject launchArguments)
    {
        return StartSession(
            session,
            debugMap,
            sourcePath,
            initializeArguments,
            () => client.BeginLaunch(launchArguments),
            DapDebugSessionPhase.Launched);
    }

    public DapDebugSessionLaunchResult Attach(
        DebugSessionCore session,
        ScriptDebugMap debugMap,
        string sourcePath,
        JsonObject? initializeArguments,
        JsonObject attachArguments)
    {
        return StartSession(
            session,
            debugMap,
            sourcePath,
            initializeArguments,
            () => client.BeginAttach(attachArguments),
            DapDebugSessionPhase.Attached);
    }

    private DapDebugSessionLaunchResult StartSession(
        DebugSessionCore session,
        ScriptDebugMap debugMap,
        string sourcePath,
        JsonObject? initializeArguments,
        Func<DapPendingRequest> beginStartRequest,
        string startedPhase)
    {
        var state = new DapDebugSessionLifecycleBuilder();
        var handshake = client.Initialize(initializeArguments);
        state.Mark(DapDebugSessionPhase.Initialized);
        var runtime = new DapDebugSessionRuntime(
            client,
            session,
            debugMap,
            handshake.Capabilities);

        var startRequest = beginStartRequest();
        var initializedEventReceived = handshake.InitializedEventReceived ||
                                       client.WaitForInitializedEvent(initializedEventTimeout);
        var breakpointResults = runtime.ApplySourceBreakpoints(sourcePath);
        state.Mark(DapDebugSessionPhase.BreakpointsConfigured);
        client.ConfigurationDone();
        state.Mark(DapDebugSessionPhase.ConfigurationDone);
        startRequest.Wait();
        state.Mark(startedPhase);
        runtime.AdoptLifecycle(state.ToLifecycle());

        return new DapDebugSessionLaunchResult(
            runtime,
            handshake.Capabilities,
            initializedEventReceived,
            breakpointResults,
            state.ToLifecycle());
    }

    private sealed class DapDebugSessionLifecycleBuilder
    {
        private readonly List<string> completedPhases = new() { DapDebugSessionPhase.Created };
        private string phase = DapDebugSessionPhase.Created;

        public void Mark(string nextPhase)
        {
            phase = nextPhase;
            completedPhases.Add(nextPhase);
        }

        public DapDebugSessionLifecycle ToLifecycle()
        {
            return new DapDebugSessionLifecycle(phase, completedPhases.ToArray());
        }
    }
}
