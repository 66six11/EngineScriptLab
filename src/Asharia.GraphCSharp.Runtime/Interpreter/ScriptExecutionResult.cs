namespace ScriptLab;

public sealed record ScriptExecutionResult(
    IReadOnlyList<RuntimeDiagnostic> Diagnostics,
    IReadOnlyList<RuntimeMutation> Mutations)
{
    public static ScriptExecutionResult FromContext(ScriptExecutionContext context)
    {
        return new ScriptExecutionResult(context.Diagnostics.ToArray(), context.Mutations.ToArray());
    }
}
