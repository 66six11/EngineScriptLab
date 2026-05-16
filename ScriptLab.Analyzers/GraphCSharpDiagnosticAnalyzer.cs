using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using ScriptLab.GraphCSharp;

namespace ScriptLab.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GraphCSharpDiagnosticAnalyzer : DiagnosticAnalyzer
{
    private static readonly IReadOnlyDictionary<string, DiagnosticDescriptor> Descriptors =
        GraphCSharpRuleSet.Diagnostics.ToDictionary(
            diagnostic => diagnostic.Id,
            diagnostic => new DiagnosticDescriptor(
                diagnostic.Id,
                diagnostic.Title,
                "{0}",
                GraphCSharpRuleSet.Category,
                DiagnosticSeverity.Error,
                isEnabledByDefault: true));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        Descriptors.Values.ToImmutableArray();

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(ReportUnsupportedSyntax, GraphCSharpRuleSet.SyntaxKinds);
        context.RegisterSyntaxNodeAction(ReportUnregisteredFunctionCall, SyntaxKind.InvocationExpression);
    }

    private static void ReportUnsupportedSyntax(SyntaxNodeAnalysisContext context)
    {
        var rule = GraphCSharpRuleSet.Find(context.Node.Kind());
        if (rule is null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors[rule.Id],
            context.Node.GetLocation(),
            rule.Message));
    }

    private static void ReportUnregisteredFunctionCall(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var csharpName = memberAccess.ToString();
        if (GraphCSharpBindingRegistry.TryGetFunctionId(csharpName, out _))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Descriptors["AGC0003"],
            invocation.GetLocation(),
            GraphCSharpBindingRegistry.GetUnregisteredFunctionCallMessage(csharpName)));
    }
}
