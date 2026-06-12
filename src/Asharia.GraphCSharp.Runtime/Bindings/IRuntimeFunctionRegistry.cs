namespace ScriptLab;

public interface IRuntimeFunctionRegistry
{
    bool TryInvoke(
        BehaviorIrCallFunction call,
        IReadOnlyList<object?> arguments,
        ScriptExecutionContext context,
        out object? result);
}

public sealed class DefaultRuntimeFunctionRegistry : IRuntimeFunctionRegistry
{
    public static DefaultRuntimeFunctionRegistry Instance { get; } = new();

    private DefaultRuntimeFunctionRegistry()
    {
    }

    public bool TryInvoke(
        BehaviorIrCallFunction call,
        IReadOnlyList<object?> arguments,
        ScriptExecutionContext context,
        out object? result)
    {
        result = null;

        switch (call.FunctionId.Value)
        {
            case "asharia.input.keyDown":
                result = ExecuteKeyDown(call, arguments, context);
                return true;

            case "asharia.transform.translate":
                ExecuteTranslate(call, arguments, context);
                return true;

            default:
                return false;
        }
    }

    private static bool ExecuteKeyDown(
        BehaviorIrCallFunction call,
        IReadOnlyList<object?> arguments,
        ScriptExecutionContext context)
    {
        if (arguments.Count != 1 || arguments[0] is not string key)
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.InvalidRuntimeValue,
                "Input.KeyDown expects one Key argument.",
                call.DebugSiteId,
                call.Source));
            return false;
        }

        return context.IsKeyDown(key);
    }

    private static void ExecuteTranslate(
        BehaviorIrCallFunction call,
        IReadOnlyList<object?> arguments,
        ScriptExecutionContext context)
    {
        if (arguments.Count != 2 ||
            arguments[0] is not ScriptRuntimeEntityRef entity ||
            arguments[1] is not ScriptRuntimeVec3 offset)
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.InvalidRuntimeValue,
                "Transform.Translate expects an EntityRef and Vec3 offset.",
                call.DebugSiteId,
                call.Source));
            return;
        }

        if (!context.IsEntityValid(entity))
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.InvalidEntity,
                $"Cannot translate invalid entity '{entity.EntityId}'.",
                call.DebugSiteId,
                call.Source));
            return;
        }

        context.Mutations.Enqueue(new TranslateMutation(
            entity,
            offset,
            call.FunctionId,
            call.DebugSiteId,
            call.Source));
    }
}
