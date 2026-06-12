namespace ScriptLab;

public abstract record RuntimeMutation(FunctionId FunctionId, string SourceSiteId, BehaviorSourceSpan Source);

public sealed record TranslateMutation(
    ScriptRuntimeEntityRef Entity,
    ScriptRuntimeVec3 Offset,
    FunctionId FunctionId,
    string SourceSiteId,
    BehaviorSourceSpan Source)
    : RuntimeMutation(FunctionId, SourceSiteId, Source);

public sealed class MutationQueue
{
    private readonly List<RuntimeMutation> mutations = new();

    public IReadOnlyList<RuntimeMutation> Mutations => mutations;

    public void Enqueue(RuntimeMutation mutation)
    {
        mutations.Add(mutation);
    }

    public RuntimeMutation[] ToArray()
    {
        return mutations.ToArray();
    }

    public void Clear()
    {
        mutations.Clear();
    }
}
