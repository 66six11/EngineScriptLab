using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace ScriptLab.GraphCSharp;

public sealed class GraphCSharpDiagnosticDefinition
{
    public GraphCSharpDiagnosticDefinition(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    public string Title { get; }
}

public sealed class GraphCSharpSyntaxRule
{
    public GraphCSharpSyntaxRule(SyntaxKind syntaxKind, string id, string title, string message)
    {
        SyntaxKind = syntaxKind;
        Id = id;
        Title = title;
        Message = message;
    }

    public SyntaxKind SyntaxKind { get; }

    public string Id { get; }

    public string Title { get; }

    public string Message { get; }
}

public static class GraphCSharpRuleSet
{
    public const string Category = "GraphCSharp";
    public const string ErrorSeverity = "Error";
    public const string UnsupportedSyntaxId = "AGC0001";
    public const string UnsupportedExpressionId = "AGC0002";
    public const string UnregisteredFunctionCallId = "AGC0003";
    public const string MissingStableFieldId = "AGC0004";
    public const string IllegalContextCallId = "AGC0005";
    public const string HiddenSideEffectId = "AGC0006";
    public const string UnsupportedLoopId = "AGC0007";
    public const string UnsupportedTypeId = "AGC0008";
    public const string UnsupportedAllocationId = "AGC0009";

    private static readonly string[] ConstructibleValueTypeNames =
    {
        "Vec2",
        "Vec3",
        "Vec4",
        "Quat",
        "Color"
    };

    private static readonly string[] ConstructibleValueTypeQualifiedNames =
    {
        "Asharia.Behavior.Vec2",
        "Asharia.Behavior.Vec3",
        "Asharia.Behavior.Vec4",
        "Asharia.Behavior.Quat",
        "Asharia.Behavior.Color"
    };

    private static readonly string[] SupportedFieldTypeNames =
    {
        "bool",
        "int",
        "float",
        "string",
        "EntityRef",
        "Key",
        "Vec2",
        "Vec3",
        "Vec4",
        "Quat",
        "Color"
    };

    private static readonly string[] SupportedFieldTypeQualifiedNames =
    {
        "bool",
        "int",
        "float",
        "string",
        "Asharia.Behavior.EntityRef",
        "Asharia.Behavior.Key",
        "Asharia.Behavior.Vec2",
        "Asharia.Behavior.Vec3",
        "Asharia.Behavior.Vec4",
        "Asharia.Behavior.Quat",
        "Asharia.Behavior.Color"
    };

    private static readonly string[] UnsupportedTypeNames =
    {
        "dynamic",
        "object",
        "Task",
        "Thread",
        "Delegate",
        "Action",
        "Func"
    };

    public static readonly GraphCSharpDiagnosticDefinition[] Diagnostics =
    {
        new(UnsupportedSyntaxId, "Unsupported Graph C# syntax"),
        new(UnsupportedExpressionId, "Unsupported Graph C# expression"),
        new(UnregisteredFunctionCallId, "Unregistered Graph C# function call"),
        new(MissingStableFieldId, "Missing stable Graph C# field id"),
        new(IllegalContextCallId, "Illegal Graph C# context call"),
        new(HiddenSideEffectId, "Hidden Graph C# side effect"),
        new(UnsupportedLoopId, "Unsupported Graph C# loop"),
        new(UnsupportedTypeId, "Unsupported Graph C# type"),
        new(UnsupportedAllocationId, "Unsupported Graph C# allocation")
    };

    public static readonly GraphCSharpSyntaxRule[] SyntaxRules =
    {
        new(
            SyntaxKind.ParenthesizedLambdaExpression,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "Lambda expressions are not supported by Graph C# v0."),
        new(
            SyntaxKind.SimpleLambdaExpression,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "Lambda expressions are not supported by Graph C# v0."),
        new(
            SyntaxKind.AnonymousMethodExpression,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "Anonymous methods are not supported by Graph C# v0."),
        new(
            SyntaxKind.AwaitExpression,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "await is not supported by Graph C# v0."),
        new(
            SyntaxKind.YieldReturnStatement,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "yield is not supported by Graph C# v0."),
        new(
            SyntaxKind.YieldBreakStatement,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "yield is not supported by Graph C# v0."),
        new(
            SyntaxKind.TryStatement,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "try/catch/finally is not supported by Graph C# v0."),
        new(
            SyntaxKind.ThrowStatement,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "throw is not supported by Graph C# v0."),
        new(
            SyntaxKind.ThrowExpression,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "throw expressions are not supported by Graph C# v0."),
        new(
            SyntaxKind.GotoStatement,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "goto is not supported by Graph C# v0."),
        new(
            SyntaxKind.LockStatement,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "lock is not supported by Graph C# v0."),
        new(
            SyntaxKind.UnsafeStatement,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "unsafe blocks are not supported by Graph C# v0."),
        new(
            SyntaxKind.PointerType,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "pointer types are not supported by Graph C# v0."),
        new(
            SyntaxKind.StackAllocArrayCreationExpression,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "stackalloc is not supported by Graph C# v0."),
        new(
            SyntaxKind.ImplicitStackAllocArrayCreationExpression,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "stackalloc is not supported by Graph C# v0."),
        new(
            SyntaxKind.DelegateDeclaration,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "delegate declarations are not supported by Graph C# v0."),
        new(
            SyntaxKind.EventDeclaration,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "events are not supported by Graph C# v0."),
        new(
            SyntaxKind.EventFieldDeclaration,
            UnsupportedSyntaxId,
            "Unsupported Graph C# syntax",
            "events are not supported by Graph C# v0."),
        new(
            SyntaxKind.ArrayCreationExpression,
            UnsupportedAllocationId,
            "Unsupported Graph C# allocation",
            "Array allocations are not supported by Graph C# v0."),
        new(
            SyntaxKind.ImplicitArrayCreationExpression,
            UnsupportedAllocationId,
            "Unsupported Graph C# allocation",
            "Implicit array allocations are not supported by Graph C# v0."),
        new(
            SyntaxKind.QueryExpression,
            UnsupportedExpressionId,
            "Unsupported Graph C# expression",
            "LINQ query expressions are not supported by Graph C# v0."),
        new(
            SyntaxKind.ForStatement,
            UnsupportedLoopId,
            "Unsupported Graph C# loop",
            "for loops are not supported by Graph C# v0."),
        new(
            SyntaxKind.ForEachStatement,
            UnsupportedLoopId,
            "Unsupported Graph C# loop",
            "foreach loops are not supported by Graph C# v0."),
        new(
            SyntaxKind.WhileStatement,
            UnsupportedLoopId,
            "Unsupported Graph C# loop",
            "while loops are not supported by Graph C# v0."),
        new(
            SyntaxKind.DoStatement,
            UnsupportedLoopId,
            "Unsupported Graph C# loop",
            "do loops are not supported by Graph C# v0.")
    };

    public static SyntaxKind[] SyntaxKinds { get; } = SyntaxRules
        .Select(rule => rule.SyntaxKind)
        .ToArray();

    public static GraphCSharpSyntaxRule? Find(SyntaxKind syntaxKind)
    {
        foreach (var rule in SyntaxRules)
        {
            if (rule.SyntaxKind == syntaxKind)
            {
                return rule;
            }
        }

        return null;
    }

    public static bool IsConstructibleValueType(string typeName)
    {
        return TryGetConstructibleValueTypeName(typeName, requireQualifiedMatch: false, out _);
    }

    public static bool IsConstructibleValueTypeSymbol(string typeName)
    {
        return TryGetConstructibleValueTypeName(typeName, requireQualifiedMatch: true, out _);
    }

    public static bool TryGetConstructibleValueTypeName(
        string typeName,
        bool requireQualifiedMatch,
        out string canonicalTypeName)
    {
        var normalizedTypeName = NormalizeTypeName(typeName);
        for (var i = 0; i < ConstructibleValueTypeNames.Length; i++)
        {
            if (string.Equals(ConstructibleValueTypeQualifiedNames[i], normalizedTypeName, StringComparison.Ordinal) ||
                (!requireQualifiedMatch &&
                 string.Equals(ConstructibleValueTypeNames[i], GetSimpleName(normalizedTypeName), StringComparison.Ordinal)))
            {
                canonicalTypeName = ConstructibleValueTypeNames[i];
                return true;
            }
        }

        canonicalTypeName = string.Empty;
        return false;
    }

    public static bool IsUnsupportedType(string typeName)
    {
        return ContainsSimpleName(UnsupportedTypeNames, typeName);
    }

    public static bool IsSupportedFieldType(string typeName)
    {
        return TryGetSupportedFieldTypeName(typeName, requireQualifiedMatch: false, out _);
    }

    public static bool IsSupportedFieldTypeSymbol(string typeName)
    {
        return TryGetSupportedFieldTypeName(typeName, requireQualifiedMatch: true, out _);
    }

    public static bool TryGetSupportedFieldTypeName(
        string typeName,
        bool requireQualifiedMatch,
        out string canonicalTypeName)
    {
        var normalizedTypeName = NormalizeTypeName(typeName);
        for (var i = 0; i < SupportedFieldTypeNames.Length; i++)
        {
            if (string.Equals(SupportedFieldTypeQualifiedNames[i], normalizedTypeName, StringComparison.Ordinal) ||
                (!requireQualifiedMatch &&
                 string.Equals(SupportedFieldTypeNames[i], GetSimpleName(normalizedTypeName), StringComparison.Ordinal)))
            {
                canonicalTypeName = SupportedFieldTypeNames[i];
                return true;
            }
        }

        canonicalTypeName = string.Empty;
        return false;
    }

    public static string GetUnsupportedTypeMessage(string typeName)
    {
        return $"Type '{typeName}' is not supported by Graph C# v0.";
    }

    public static string GetUnsupportedAllocationMessage(string typeName)
    {
        return $"Allocation 'new {typeName}' is not supported by Graph C# v0. Use registered value structs or graph API factories.";
    }

    public static string GetUnsupportedImplicitAllocationMessage()
    {
        return "Implicit object creation is not supported by Graph C# v0. Use an explicit registered value struct constructor.";
    }

    public static string GetUnsupportedReflectionMessage(string expression)
    {
        return $"Reflection expression '{expression}' is not supported by Graph C# v0.";
    }

    public static string GetUnsupportedStaticStateMessage(string fieldName)
    {
        return $"Static field '{fieldName}' is not supported by Graph C# v0 because graph scripts cannot own global mutable state.";
    }

    public static string GetMissingStableFieldIdMessage(string fieldName)
    {
        return $"Field '{fieldName}' must declare a stable Graph C# field id with [Field(id)].";
    }

    public static string GetInvalidFunctionArgumentCountMessage(
        string csharpName,
        int expectedCount,
        int actualCount)
    {
        return $"Function call '{csharpName}' expects {expectedCount} argument(s), but received {actualCount}.";
    }

    public static string GetInvalidFunctionArgumentTypeMessage(
        string csharpName,
        int argumentIndex,
        string expectedType,
        string actualType)
    {
        return $"Function call '{csharpName}' argument {argumentIndex} expects '{expectedType}', but received '{actualType}'.";
    }

    public static string GetIllegalContextCallMessage(string csharpName, string contextName)
    {
        return $"Function call '{csharpName}' is not allowed in Graph C# context '{contextName}'.";
    }

    public static string GetHiddenSideEffectMessage(string csharpName)
    {
        return $"Function call '{csharpName}' has side effects and must be used as a standalone statement.";
    }

    private static bool ContainsSimpleName(string[] names, string typeName)
    {
        var simpleName = GetSimpleName(typeName);
        foreach (var name in names)
        {
            if (string.Equals(name, simpleName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetSimpleName(string typeName)
    {
        var normalized = NormalizeTypeName(typeName);
        var genericStart = normalized.IndexOf('<');
        if (genericStart >= 0)
        {
            normalized = normalized.Substring(0, genericStart);
        }

        var aliasSeparator = normalized.LastIndexOf("::", StringComparison.Ordinal);
        if (aliasSeparator >= 0)
        {
            normalized = normalized.Substring(aliasSeparator + 2);
        }

        var namespaceSeparator = normalized.LastIndexOf('.');
        if (namespaceSeparator >= 0)
        {
            normalized = normalized.Substring(namespaceSeparator + 1);
        }

        return normalized;
    }

    private static string NormalizeTypeName(string typeName)
    {
        const string globalPrefix = "global::";
        var normalized = typeName.Trim();
        return normalized.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? normalized.Substring(globalPrefix.Length)
            : normalized;
    }
}
