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

    public static readonly GraphCSharpDiagnosticDefinition[] Diagnostics =
    {
        new("AGC0001", "Unsupported Graph C# syntax"),
        new("AGC0002", "Unsupported Graph C# expression"),
        new("AGC0003", "Unregistered Graph C# function call"),
        new("AGC0007", "Unsupported Graph C# loop")
    };

    public static readonly GraphCSharpSyntaxRule[] SyntaxRules =
    {
        new(
            SyntaxKind.ParenthesizedLambdaExpression,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "Lambda expressions are not supported by Graph C# v0."),
        new(
            SyntaxKind.SimpleLambdaExpression,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "Lambda expressions are not supported by Graph C# v0."),
        new(
            SyntaxKind.AnonymousMethodExpression,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "Anonymous methods are not supported by Graph C# v0."),
        new(
            SyntaxKind.AwaitExpression,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "await is not supported by Graph C# v0."),
        new(
            SyntaxKind.YieldReturnStatement,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "yield is not supported by Graph C# v0."),
        new(
            SyntaxKind.YieldBreakStatement,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "yield is not supported by Graph C# v0."),
        new(
            SyntaxKind.TryStatement,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "try/catch/finally is not supported by Graph C# v0."),
        new(
            SyntaxKind.ThrowStatement,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "throw is not supported by Graph C# v0."),
        new(
            SyntaxKind.GotoStatement,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "goto is not supported by Graph C# v0."),
        new(
            SyntaxKind.LockStatement,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "lock is not supported by Graph C# v0."),
        new(
            SyntaxKind.UnsafeStatement,
            "AGC0001",
            "Unsupported Graph C# syntax",
            "unsafe blocks are not supported by Graph C# v0."),
        new(
            SyntaxKind.QueryExpression,
            "AGC0002",
            "Unsupported Graph C# expression",
            "LINQ query expressions are not supported by Graph C# v0."),
        new(
            SyntaxKind.ForStatement,
            "AGC0007",
            "Unsupported Graph C# loop",
            "for loops are not supported by Graph C# v0."),
        new(
            SyntaxKind.ForEachStatement,
            "AGC0007",
            "Unsupported Graph C# loop",
            "foreach loops are not supported by Graph C# v0."),
        new(
            SyntaxKind.WhileStatement,
            "AGC0007",
            "Unsupported Graph C# loop",
            "while loops are not supported by Graph C# v0."),
        new(
            SyntaxKind.DoStatement,
            "AGC0007",
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
}
