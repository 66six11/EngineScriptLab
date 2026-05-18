namespace ScriptLab.GraphCSharp;

public sealed class GraphCSharpFunctionBinding
{
    public GraphCSharpFunctionBinding(string csharpName, string functionId)
    {
        CSharpName = csharpName;
        FunctionId = functionId;
    }

    public string CSharpName { get; }

    public string FunctionId { get; }
}

public static class GraphCSharpBindingRegistry
{
    public static readonly GraphCSharpFunctionBinding[] Functions =
    {
        new("Input.KeyDown", "asharia.input.keyDown"),
        new("Transform.Translate", "asharia.transform.translate"),
        new("GraphDebug.Inspect", "asharia.debug.inspect"),
        new("GraphDebug.Watch", "asharia.debug.watch")
    };

    public static bool TryGetFunctionId(string csharpName, out string functionId)
    {
        foreach (var binding in Functions)
        {
            if (binding.CSharpName == csharpName)
            {
                functionId = binding.FunctionId;
                return true;
            }
        }

        functionId = string.Empty;
        return false;
    }

    public static string GetUnregisteredFunctionCallMessage(string csharpName)
    {
        return $"Function call '{csharpName}' is not registered in Graph C# BindingRegistry.";
    }
}
