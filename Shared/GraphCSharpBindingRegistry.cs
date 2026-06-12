using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab;

namespace ScriptLab.GraphCSharp;

public sealed class GraphCSharpFunctionBinding
{
    public GraphCSharpFunctionBinding(string csharpName, string csharpQualifiedName, FunctionId functionId)
    {
        CSharpName = csharpName;
        CSharpQualifiedName = csharpQualifiedName;
        FunctionId = functionId;
    }

    public string CSharpName { get; }

    public string CSharpQualifiedName { get; }

    public FunctionId FunctionId { get; }
}

public static class GraphCSharpBindingRegistry
{
    public static readonly GraphCSharpFunctionBinding[] Functions =
    {
        new("Input.KeyDown", "Asharia.Behavior.Input.KeyDown", new FunctionId("asharia.input.keyDown")),
        new("Transform.Translate", "Asharia.Behavior.Transform.Translate", new FunctionId("asharia.transform.translate")),
        new("GraphDebug.Inspect", "Asharia.Behavior.GraphDebug.Inspect", new FunctionId("asharia.debug.inspect")),
        new("GraphDebug.Watch", "Asharia.Behavior.GraphDebug.Watch", new FunctionId("asharia.debug.watch"))
    };

    public static bool TryGetFunctionId(string csharpName, out FunctionId functionId)
    {
        var normalizedName = NormalizeCSharpName(csharpName);
        foreach (var binding in Functions)
        {
            if (binding.CSharpName == normalizedName ||
                binding.CSharpQualifiedName == normalizedName)
            {
                functionId = binding.FunctionId;
                return true;
            }
        }

        functionId = default;
        return false;
    }

    public static bool TryResolveFunctionId(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        out FunctionId functionId,
        out string csharpName)
    {
        if (TryGetSemanticCSharpName(invocation, semanticModel, out csharpName))
        {
            return TryGetFunctionId(csharpName, out functionId);
        }

        csharpName = invocation.Expression.ToString();
        return TryGetFunctionId(csharpName, out functionId);
    }

    public static string GetUnregisteredFunctionCallMessage(string csharpName)
    {
        return $"Function call '{csharpName}' is not registered in Graph C# BindingRegistry.";
    }

    private static bool TryGetSemanticCSharpName(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        out string csharpName)
    {
        if (semanticModel is null)
        {
            csharpName = string.Empty;
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var method = symbolInfo.Symbol as IMethodSymbol ??
                     symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (method?.ContainingType is null)
        {
            csharpName = string.Empty;
            return false;
        }

        csharpName = $"{method.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}.{method.Name}";
        return true;
    }

    private static string NormalizeCSharpName(string csharpName)
    {
        const string globalPrefix = "global::";
        return csharpName.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? csharpName.Substring(globalPrefix.Length)
            : csharpName;
    }
}
