using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab;

namespace ScriptLab.GraphCSharp;

public sealed class GraphCSharpParameterBinding
{
    public GraphCSharpParameterBinding(string name, string typeName)
    {
        Name = name;
        TypeName = typeName;
    }

    public string Name { get; }

    public string TypeName { get; }
}

[Flags]
public enum GraphCSharpEffectFlags
{
    None = 0,
    ReadsInput = 1,
    MutatesWorld = 2,
    Debug = 4
}

[Flags]
public enum GraphCSharpScriptContextMask
{
    None = 0,
    Start = 1,
    Update = 2,
    FixedUpdate = 4,
    Destroy = 8,
    AnyLifecycle = Start | Update | FixedUpdate | Destroy
}

public sealed class GraphCSharpFunctionBinding
{
    public GraphCSharpFunctionBinding(
        string csharpName,
        string csharpQualifiedName,
        FunctionId functionId,
        string? returnType,
        IReadOnlyList<GraphCSharpParameterBinding> parameters,
        GraphCSharpEffectFlags effects,
        GraphCSharpScriptContextMask allowedContexts,
        string displayName,
        string category,
        int version,
        FunctionId? replacedBy = null)
    {
        CSharpName = csharpName;
        CSharpQualifiedName = csharpQualifiedName;
        FunctionId = functionId;
        ReturnType = returnType;
        Parameters = parameters;
        Effects = effects;
        AllowedContexts = allowedContexts;
        DisplayName = displayName;
        Category = category;
        Version = version;
        ReplacedBy = replacedBy;
    }

    public string CSharpName { get; }

    public string CSharpQualifiedName { get; }

    public FunctionId FunctionId { get; }

    public string? ReturnType { get; }

    public IReadOnlyList<GraphCSharpParameterBinding> Parameters { get; }

    public GraphCSharpEffectFlags Effects { get; }

    public GraphCSharpScriptContextMask AllowedContexts { get; }

    public string DisplayName { get; }

    public string Category { get; }

    public int Version { get; }

    public FunctionId? ReplacedBy { get; }
}

public static class GraphCSharpBindingRegistry
{
    public static readonly GraphCSharpFunctionBinding[] Functions =
    {
        new(
            "Input.KeyDown",
            "Asharia.Behavior.Input.KeyDown",
            new FunctionId("asharia.input.keyDown"),
            "bool",
            new[] { new GraphCSharpParameterBinding("key", "Key") },
            GraphCSharpEffectFlags.ReadsInput,
            GraphCSharpScriptContextMask.Update | GraphCSharpScriptContextMask.FixedUpdate,
            "Key Down",
            "Input",
            1),
        new(
            "Transform.Translate",
            "Asharia.Behavior.Transform.Translate",
            new FunctionId("asharia.transform.translate"),
            null,
            new[]
            {
                new GraphCSharpParameterBinding("entity", "EntityRef"),
                new GraphCSharpParameterBinding("offset", "Vec3")
            },
            GraphCSharpEffectFlags.MutatesWorld,
            GraphCSharpScriptContextMask.Update | GraphCSharpScriptContextMask.FixedUpdate,
            "Translate",
            "Transform",
            1),
        new(
            "GraphDebug.Inspect",
            "Asharia.Behavior.GraphDebug.Inspect",
            new FunctionId("asharia.debug.inspect"),
            "*",
            new[]
            {
                new GraphCSharpParameterBinding("name", "string"),
                new GraphCSharpParameterBinding("value", "*")
            },
            GraphCSharpEffectFlags.Debug,
            GraphCSharpScriptContextMask.AnyLifecycle,
            "Inspect",
            "Debug",
            1),
        new(
            "GraphDebug.Watch",
            "Asharia.Behavior.GraphDebug.Watch",
            new FunctionId("asharia.debug.watch"),
            null,
            new[]
            {
                new GraphCSharpParameterBinding("name", "string"),
                new GraphCSharpParameterBinding("value", "*")
            },
            GraphCSharpEffectFlags.Debug,
            GraphCSharpScriptContextMask.AnyLifecycle,
            "Watch",
            "Debug",
            1)
    };

    public static bool TryGetFunctionId(string csharpName, out FunctionId functionId)
    {
        if (TryGetFunctionBinding(csharpName, out var binding))
        {
            functionId = binding.FunctionId;
            return true;
        }

        functionId = default;
        return false;
    }

    public static bool TryGetFunctionBinding(string csharpName, out GraphCSharpFunctionBinding binding)
    {
        var normalizedName = NormalizeCSharpName(csharpName);
        foreach (var candidate in Functions)
        {
            if (candidate.CSharpName == normalizedName ||
                candidate.CSharpQualifiedName == normalizedName)
            {
                binding = candidate;
                return true;
            }
        }

        binding = null!;
        return false;
    }

    public static bool TryResolveFunctionId(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        out FunctionId functionId,
        out string csharpName)
    {
        if (TryResolveFunctionBinding(invocation, semanticModel, out var binding, out csharpName))
        {
            functionId = binding.FunctionId;
            return true;
        }

        functionId = default;
        return false;
    }

    public static bool TryResolveFunctionBinding(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        out GraphCSharpFunctionBinding binding,
        out string csharpName)
    {
        if (TryGetSemanticCSharpName(invocation, semanticModel, out csharpName))
        {
            return TryGetFunctionBinding(csharpName, out binding);
        }

        csharpName = invocation.Expression.ToString();
        return TryGetFunctionBinding(csharpName, out binding);
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
