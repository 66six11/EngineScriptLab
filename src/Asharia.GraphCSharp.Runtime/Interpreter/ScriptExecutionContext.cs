namespace ScriptLab;

public sealed class ScriptExecutionContext
{
    private readonly List<RuntimeDiagnostic> diagnostics = new();
    private readonly IReadOnlySet<int>? validEntityIds;

    public ScriptExecutionContext(
        IReadOnlySet<string>? downKeys = null,
        IReadOnlySet<int>? validEntityIds = null,
        MutationQueue? mutationQueue = null,
        IRuntimeFunctionRegistry? functionRegistry = null,
        int stepLimit = 1024)
    {
        DownKeys = downKeys ?? new HashSet<string>(StringComparer.Ordinal);
        this.validEntityIds = validEntityIds;
        Mutations = mutationQueue ?? new MutationQueue();
        FunctionRegistry = functionRegistry ?? DefaultRuntimeFunctionRegistry.Instance;
        StepLimit = stepLimit;
    }

    public IReadOnlySet<string> DownKeys { get; }

    public MutationQueue Mutations { get; }

    public IRuntimeFunctionRegistry FunctionRegistry { get; }

    public int StepLimit { get; }

    public IReadOnlyList<RuntimeDiagnostic> Diagnostics => diagnostics;

    public bool IsKeyDown(string key)
    {
        return DownKeys.Contains(key);
    }

    public bool IsEntityValid(ScriptRuntimeEntityRef entity)
    {
        return validEntityIds is null || validEntityIds.Contains(entity.EntityId);
    }

    public void Report(RuntimeDiagnostic diagnostic)
    {
        diagnostics.Add(diagnostic);
    }
}
