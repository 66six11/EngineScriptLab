namespace ScriptLab;

public sealed record DapDebugRuntimeEventDrain(
    IReadOnlyList<ScriptStoppedEvent> StoppedEvents,
    IReadOnlyList<DapLifecycleEvent> LifecycleEvents,
    IReadOnlyList<DapBreakpointEvent> BreakpointEvents,
    ScriptStoppedEvent? CurrentStoppedEvent,
    DapDebugSessionLifecycle Lifecycle);

public sealed class DapDebugSessionRuntime
{
    private readonly DapDebugSessionClient client;
    private readonly ScriptDebugSession session;
    private readonly DapScriptBreakpointBackend breakpointBackend;
    private readonly DapScriptStoppedEventResolver stoppedEventResolver;
    private readonly DapScriptFrameVariableBackend frameVariableBackend;
    private readonly HashSet<ScriptStoppedEvent> currentStoppedEvents = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> completedPhases = new();
    private string phase = DapDebugSessionPhase.Created;

    public DapDebugSessionRuntime(
        IDapRequestClient client,
        ScriptDebugSession session,
        ScriptDebugMap debugMap,
        DapBreakpointBackendCapabilities? capabilities = null)
        : this(new DapDebugSessionClient(client), session, debugMap, capabilities)
    {
    }

    public DapDebugSessionRuntime(
        DapDebugSessionClient client,
        ScriptDebugSession session,
        ScriptDebugMap debugMap,
        DapBreakpointBackendCapabilities? capabilities = null)
    {
        this.client = client;
        this.session = session;
        breakpointBackend = new DapScriptBreakpointBackend(client, capabilities);
        stoppedEventResolver = new DapScriptStoppedEventResolver(client, session);
        frameVariableBackend = new DapScriptFrameVariableBackend(client, debugMap);
    }

    public IScriptBreakpointBackend BreakpointBackend => breakpointBackend;

    public IScriptFrameVariableBackend FrameVariableBackend => frameVariableBackend;

    public DapDebugSessionLifecycle Lifecycle => new(phase, completedPhases.ToArray());

    public bool HasCurrentStoppedEvent => currentStoppedEvents.Count > 0;

    public bool IsCurrentStoppedEvent(ScriptStoppedEvent stoppedEvent)
    {
        return currentStoppedEvents.Contains(stoppedEvent);
    }

    public void AdoptLifecycle(DapDebugSessionLifecycle lifecycle)
    {
        phase = lifecycle.Phase;
        completedPhases.Clear();
        completedPhases.AddRange(lifecycle.CompletedPhases);
    }

    public IReadOnlyList<ScriptBreakpointBackendResult> ApplySourceBreakpoints(string sourcePath)
    {
        return session.ApplySourceBreakpoints(breakpointBackend, sourcePath);
    }

    public IReadOnlyList<ScriptStoppedEvent> DrainStoppedEvents()
    {
        return DrainDebugEvents().StoppedEvents;
    }

    public IReadOnlyList<DapLifecycleEvent> DrainLifecycleEvents()
    {
        return DrainDebugEvents().LifecycleEvents;
    }

    public DapDebugRuntimeEventDrain DrainDebugEvents()
    {
        var drain = client.DrainDebugEvents();
        var stoppedEvents = stoppedEventResolver.ResolveStoppedEvents(drain.StoppedEvents);
        var stoppedIndex = 0;
        ScriptStoppedEvent? currentStoppedEvent = null;

        foreach (var debugEvent in drain.Events)
        {
            if (debugEvent.LifecycleEvent is not null)
            {
                currentStoppedEvents.Clear();
                currentStoppedEvent = null;
                MarkLifecycle(debugEvent.LifecycleEvent.Kind);
                continue;
            }

            if (debugEvent.StoppedEvent is not null)
            {
                var stoppedEvent = stoppedEvents[stoppedIndex++];
                currentStoppedEvents.Clear();
                currentStoppedEvents.Add(stoppedEvent);
                currentStoppedEvent = stoppedEvent;
                Mark(DapDebugSessionPhase.Stopped);
            }
        }

        return new DapDebugRuntimeEventDrain(
            stoppedEvents,
            drain.LifecycleEvents,
            drain.BreakpointEvents,
            currentStoppedEvent,
            Lifecycle);
    }

    public DapContinueResult Continue(int threadId)
    {
        var result = client.Continue(threadId);
        currentStoppedEvents.Clear();
        Mark(DapDebugSessionPhase.Running);
        return result;
    }

    public void Next(int threadId, string? granularity = null)
    {
        client.Next(threadId, granularity);
        currentStoppedEvents.Clear();
        Mark(DapDebugSessionPhase.Running);
    }

    public ScriptPausedSnapshot ReadPausedSnapshot(
        DebugScriptHost host,
        ScriptStoppedEvent stoppedEvent,
        int entityId)
    {
        if (!currentStoppedEvents.Contains(stoppedEvent))
        {
            throw new InvalidOperationException(
                "The stopped event is no longer current. Drain a new stopped event before reading paused frame variables.");
        }

        return session.ReadPausedSnapshot(host, stoppedEvent, entityId, frameVariableBackend);
    }

    public IReadOnlyList<DapVariable> ReadVariables(int variablesReference)
    {
        if (!HasCurrentStoppedEvent)
        {
            throw new InvalidOperationException(
                "The DAP paused state is no longer current. Drain a new stopped event before reading variables.");
        }

        return variablesReference <= 0
            ? Array.Empty<DapVariable>()
            : client.Variables(variablesReference);
    }

    public void Disconnect(bool terminateDebuggee = true)
    {
        client.Disconnect(terminateDebuggee);
        currentStoppedEvents.Clear();
        Mark(DapDebugSessionPhase.Disconnected);
    }

    private void MarkLifecycle(string lifecycleKind)
    {
        Mark(lifecycleKind switch
        {
            DapLifecycleEventKind.Exited => DapDebugSessionPhase.Exited,
            DapLifecycleEventKind.Terminated => DapDebugSessionPhase.Terminated,
            _ => lifecycleKind
        });
    }

    private void Mark(string nextPhase)
    {
        phase = nextPhase;
        completedPhases.Add(nextPhase);
    }
}
