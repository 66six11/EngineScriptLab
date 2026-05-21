namespace ScriptLab;

public sealed class DapDebugSessionRuntime
{
    private readonly ScriptDebugSession session;
    private readonly DapScriptBreakpointBackend breakpointBackend;
    private readonly DapScriptStoppedEventResolver stoppedEventResolver;
    private readonly DapScriptFrameVariableBackend frameVariableBackend;

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
        return stoppedEventResolver.ResolveDrainedStoppedEvents();
    }

    public ScriptPausedSnapshot ReadPausedSnapshot(
        DebugScriptHost host,
        ScriptStoppedEvent stoppedEvent,
        int entityId)
    {
        return session.ReadPausedSnapshot(host, stoppedEvent, entityId, frameVariableBackend);
    }
}
