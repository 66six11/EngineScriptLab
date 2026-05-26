namespace ScriptLab;

public sealed class DapScriptFrameVariableBackend : IScriptFrameVariableBackend
{
    private readonly DapDebugSessionClient client;
    private readonly ScriptDebugMap debugMap;

    public DapScriptFrameVariableBackend(IDapRequestClient client, ScriptDebugMap debugMap)
        : this(new DapDebugSessionClient(client), debugMap)
    {
    }

    public DapScriptFrameVariableBackend(DapDebugSessionClient client, ScriptDebugMap debugMap)
    {
        this.client = client;
        this.debugMap = debugMap;
    }

    public ScriptFrameVariableSnapshot ReadVariables(ScriptStoppedEvent stoppedEvent)
    {
        if (stoppedEvent.ThreadId is null)
        {
            return Unavailable("DAP stopped event did not include a thread id.");
        }

        var frames = client.StackTrace(stoppedEvent.ThreadId.Value);
        var frame = SelectUserScriptFrame(frames, stoppedEvent);
        if (frame is null)
        {
            return Unavailable("DAP stack trace did not include a frame for the current script source.");
        }

        var scopes = client.Scopes(frame.Id);
        return new ScriptFrameVariableSnapshot(
            Available: true,
            Reason: $"Read DAP frame variables from frame '{frame.Name}'.",
            Arguments: ReadScopeVariables(scopes, "Arguments", ScriptDebugScopeKind.Arguments),
            Locals: ReadScopeVariables(scopes, "Locals", ScriptDebugScopeKind.Locals),
            ThisVariables: ReadScopeVariables(scopes, "This", ScriptDebugScopeKind.This));
    }

    private DapStackFrame? SelectUserScriptFrame(
        IReadOnlyList<DapStackFrame> frames,
        ScriptStoppedEvent stoppedEvent)
    {
        var sourcePath = stoppedEvent.SourcePath ?? debugMap.SourceDocumentPath;
        var sourceMatches = frames
            .Where(frame => !string.IsNullOrWhiteSpace(frame.SourcePath) &&
                            PathsEqual(frame.SourcePath!, sourcePath))
            .ToArray();
        if (sourceMatches.Length == 0)
        {
            return null;
        }

        if (stoppedEvent.Line is not null)
        {
            var lineMatch = sourceMatches.FirstOrDefault(frame => frame.Line == stoppedEvent.Line.Value);
            if (lineMatch is not null)
            {
                return lineMatch;
            }
        }

        return sourceMatches[0];
    }

    private IReadOnlyList<ScriptDebugVariable> ReadScopeVariables(
        IReadOnlyList<DapScope> scopes,
        string dapScopeName,
        string scopeKind)
    {
        var scope = scopes.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, dapScopeName, StringComparison.OrdinalIgnoreCase));
        if (scope is null || scope.VariablesReference <= 0)
        {
            return Array.Empty<ScriptDebugVariable>();
        }

        return client.Variables(scope.VariablesReference)
            .Select(variable => new ScriptDebugVariable(
                variable.Name,
                $"dap:{scopeKind}:{scope.VariablesReference}:{variable.Name}",
                variable.Type,
                variable.Value,
                RawValue: null,
                BehaviorId: scopeKind == ScriptDebugScopeKind.This ? debugMap.BehaviorId : null,
                EntityId: null,
                FieldId: scopeKind == ScriptDebugScopeKind.This ? variable.Name : null,
                Accessibility: null,
                Serialization: null,
                Writable: false,
                DebugSiteId: null,
                GraphNodeId: null,
                FunctionId: null,
                ProbeId: null,
                PinId: null,
                Sequence: null,
                HitCount: null)
            {
                VariablesReference = variable.VariablesReference > 0
                    ? variable.VariablesReference
                    : null
            })
            .ToArray();
    }

    private static ScriptFrameVariableSnapshot Unavailable(string reason)
    {
        return new ScriptFrameVariableSnapshot(
            Available: false,
            reason,
            Arguments: Array.Empty<ScriptDebugVariable>(),
            Locals: Array.Empty<ScriptDebugVariable>(),
            ThisVariables: Array.Empty<ScriptDebugVariable>());
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
