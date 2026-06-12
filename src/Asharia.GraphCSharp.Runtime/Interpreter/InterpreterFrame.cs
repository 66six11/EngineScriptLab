namespace ScriptLab;

public sealed class InterpreterFrame
{
    private readonly Dictionary<string, object?> locals;
    private readonly Dictionary<string, object?> temps = new(StringComparer.Ordinal);

    internal InterpreterFrame(
        BehaviorIrFunction function,
        IReadOnlyDictionary<string, object?> parameters)
    {
        Function = function;
        locals = new Dictionary<string, object?>(parameters, StringComparer.Ordinal);
    }

    public BehaviorIrFunction Function { get; }

    internal void DeclareLocal(string name, object? value)
    {
        locals[name] = value;
    }

    internal bool TryGetLocal(string name, out object? value)
    {
        return locals.TryGetValue(name, out value);
    }

    internal void SetLocal(string name, object? value)
    {
        locals[name] = value;
    }

    internal void SetTemp(string name, object? value)
    {
        temps[name] = value;
    }

    internal bool TryGetTemp(string name, out object? value)
    {
        return temps.TryGetValue(name, out value);
    }
}
