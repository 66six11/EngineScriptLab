using ScriptLab;

namespace ScriptLab.GraphCSharp;

public sealed class GraphCSharpFunctionBinding
{
    public GraphCSharpFunctionBinding(string csharpName, FunctionId functionId)
    {
        CSharpName = csharpName;
        FunctionId = functionId;
    }

    public string CSharpName { get; }

    public FunctionId FunctionId { get; }
}

public static class GraphCSharpBindingRegistry
{
    public static readonly GraphCSharpFunctionBinding[] Functions =
    {
        new("Input.KeyDown", new FunctionId("asharia.input.keyDown")),
        new("Transform.Translate", new FunctionId("asharia.transform.translate")),
        new("GraphDebug.Inspect", new FunctionId("asharia.debug.inspect")),
        new("GraphDebug.Watch", new FunctionId("asharia.debug.watch"))
    };

    public static bool TryGetFunctionId(string csharpName, out FunctionId functionId)
    {
        foreach (var binding in Functions)
        {
            if (binding.CSharpName == csharpName)
            {
                functionId = binding.FunctionId;
                return true;
            }
        }

        functionId = default;
        return false;
    }

    public static string GetUnregisteredFunctionCallMessage(string csharpName)
    {
        return $"Function call '{csharpName}' is not registered in Graph C# BindingRegistry.";
    }
}
