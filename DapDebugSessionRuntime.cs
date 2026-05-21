namespace ScriptLab;

public sealed class DapDebugSessionRuntime
{
    private readonly DapDebugSessionClient client;
    private readonly ScriptDebugSession session;
    private readonly DapScriptBreakpointBackend breakpointBackend;
    private readonly DapScriptStoppedEventResolver stoppedEventResolver;
    private readonly DapScriptFrameVariableBackend frameVariableBackend;
    private readonly HashSet<ScriptStoppedEvent> currentStoppedEvents = new(ReferenceEqualityComparer.Instance);

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

    public IReadOnlyList<ScriptBreakpointBackendResult> ApplySourceBreakpoints(string sourcePath)
    {
        return session.ApplySourceBreakpoints(breakpointBackend, sourcePath);
    }

    public IReadOnlyList<ScriptStoppedEvent> DrainStoppedEvents()
    {
        var stoppedEvents = stoppedEventResolver.ResolveDrainedStoppedEvents();
        if (stoppedEvents.Count > 0)
        {
            currentStoppedEvents.Clear();
            foreach (var stoppedEvent in stoppedEvents)
            {
                currentStoppedEvents.Add(stoppedEvent);
            }
        }

        return stoppedEvents;
    }

    public IReadOnlyList<DapLifecycleEvent> DrainLifecycleEvents()
    {
        var lifecycleEvents = client.DrainLifecycleEvents();
        if (lifecycleEvents.Count > 0)
        {
            currentStoppedEvents.Clear();
        }

        return lifecycleEvents;
    }

    public DapContinueResult Continue(int threadId)
    {
        var result = client.Continue(threadId);
        currentStoppedEvents.Clear();
        return result;
    }

    public void Next(int threadId, string? granularity = null)
    {
        client.Next(threadId, granularity);
        currentStoppedEvents.Clear();
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

    public void Disconnect(bool terminateDebuggee = true)
    {
        client.Disconnect(terminateDebuggee);
        currentStoppedEvents.Clear();
    }
}
