using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ScriptLab.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GraphCSharpDiagnosticAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor UnsupportedSyntax = new(
        "AGC0001",
        "Unsupported Graph C# syntax",
        "{0}",
        "GraphCSharp",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedExpression = new(
        "AGC0002",
        "Unsupported Graph C# expression",
        "{0}",
        "GraphCSharp",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedLoop = new(
        "AGC0007",
        "Unsupported Graph C# loop",
        "{0}",
        "GraphCSharp",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(UnsupportedSyntax, UnsupportedExpression, UnsupportedLoop);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            ReportUnsupportedSyntax,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.AnonymousMethodExpression,
            SyntaxKind.AwaitExpression,
            SyntaxKind.YieldReturnStatement,
            SyntaxKind.YieldBreakStatement,
            SyntaxKind.TryStatement,
            SyntaxKind.ThrowStatement,
            SyntaxKind.GotoStatement,
            SyntaxKind.LockStatement,
            SyntaxKind.UnsafeStatement);

        context.RegisterSyntaxNodeAction(
            ReportUnsupportedExpression,
            SyntaxKind.QueryExpression);

        context.RegisterSyntaxNodeAction(
            ReportUnsupportedLoop,
            SyntaxKind.ForStatement,
            SyntaxKind.ForEachStatement,
            SyntaxKind.WhileStatement,
            SyntaxKind.DoStatement);
    }

    private static void ReportUnsupportedSyntax(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            UnsupportedSyntax,
            context.Node.GetLocation(),
            GetUnsupportedSyntaxMessage(context.Node)));
    }

    private static void ReportUnsupportedExpression(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            UnsupportedExpression,
            context.Node.GetLocation(),
            "LINQ query expressions are not supported by Graph C# v0."));
    }

    private static void ReportUnsupportedLoop(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            UnsupportedLoop,
            context.Node.GetLocation(),
            GetUnsupportedLoopMessage(context.Node)));
    }

    private static string GetUnsupportedSyntaxMessage(SyntaxNode node)
    {
        return node switch
        {
            LambdaExpressionSyntax => "Lambda expressions are not supported by Graph C# v0.",
            AnonymousMethodExpressionSyntax => "Anonymous methods are not supported by Graph C# v0.",
            AwaitExpressionSyntax => "await is not supported by Graph C# v0.",
            YieldStatementSyntax => "yield is not supported by Graph C# v0.",
            TryStatementSyntax => "try/catch/finally is not supported by Graph C# v0.",
            ThrowStatementSyntax => "throw is not supported by Graph C# v0.",
            GotoStatementSyntax => "goto is not supported by Graph C# v0.",
            LockStatementSyntax => "lock is not supported by Graph C# v0.",
            UnsafeStatementSyntax => "unsafe blocks are not supported by Graph C# v0.",
            _ => "This syntax is not supported by Graph C# v0."
        };
    }

    private static string GetUnsupportedLoopMessage(SyntaxNode node)
    {
        return node switch
        {
            ForStatementSyntax => "for loops are not supported by Graph C# v0.",
            ForEachStatementSyntax => "foreach loops are not supported by Graph C# v0.",
            WhileStatementSyntax => "while loops are not supported by Graph C# v0.",
            DoStatementSyntax => "do loops are not supported by Graph C# v0.",
            _ => "Loops are not supported by Graph C# v0."
        };
    }
}
