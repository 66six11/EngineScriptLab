namespace ScriptLab;

public sealed class DapScriptStoppedEventResolver
{
    private readonly DapDebugSessionClient client;
    private readonly ScriptDebugSession session;

    public DapScriptStoppedEventResolver(IDapRequestClient client, ScriptDebugSession session)
        : this(new DapDebugSessionClient(client), session)
    {
    }

    public DapScriptStoppedEventResolver(DapDebugSessionClient client, ScriptDebugSession session)
    {
        this.client = client;
        this.session = session;
    }

    public ScriptStoppedEvent Resolve(DapStoppedEvent stoppedEvent)
    {
        if (stoppedEvent.ThreadId is null)
        {
            return CreateUnresolved(
                stoppedEvent,
                "DAP stopped event did not include a thread id.");
        }

        var frame = client.StackTrace(stoppedEvent.ThreadId.Value)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.SourcePath) &&
                                PathsEqual(candidate.SourcePath!, session.SourceDocumentPath))
            .FirstOrDefault();

        if (frame is null)
        {
            return CreateUnresolved(
                stoppedEvent,
                "DAP stack trace did not include a frame for the current script source.");
        }

        return session.ResolveDebuggerStoppedFrame(
            stoppedEvent.Reason,
            stoppedEvent.AllThreadsStopped,
            stoppedEvent.ThreadId,
            frame.SourcePath!,
            frame.Line,
            frame.Column);
    }

    public IReadOnlyList<ScriptStoppedEvent> ResolveDrainedStoppedEvents()
    {
        return client.DrainStoppedEvents()
            .Select(Resolve)
            .ToArray();
    }

    public IReadOnlyList<ScriptStoppedEvent> ResolveStoppedEvents(IReadOnlyList<DapStoppedEvent> stoppedEvents)
    {
        return stoppedEvents
            .Select(Resolve)
            .ToArray();
    }

    private static ScriptStoppedEvent CreateUnresolved(DapStoppedEvent stoppedEvent, string message)
    {
        return new ScriptStoppedEvent(
            ScriptStoppedEventStatus.Unresolved,
            NormalizeStoppedReason(stoppedEvent.Reason),
            stoppedEvent.AllThreadsStopped,
            Synthetic: false,
            stoppedEvent.ThreadId,
            ProbeId: null,
            DebugSiteId: null,
            GraphNodeId: null,
            FunctionId: null,
            SourcePath: null,
            Line: null,
            Column: null,
            PdbSequencePoint: null,
            Binding: null,
            message);
    }

    private static string NormalizeStoppedReason(string reason)
    {
        return reason switch
        {
            ScriptStoppedReason.Breakpoint => ScriptStoppedReason.Breakpoint,
            ScriptStoppedReason.Step => ScriptStoppedReason.Step,
            ScriptStoppedReason.Pause => ScriptStoppedReason.Pause,
            _ => ScriptStoppedReason.Unknown
        };
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
