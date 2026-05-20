using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
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

        context.RegisterSyntaxNodeAction(ReportRestriction, GraphCSharpRestrictionAnalyzer.SyntaxKinds);
    }

    private static void ReportRestriction(SyntaxNodeAnalysisContext context)
    {
        foreach (var diagnostic in GraphCSharpRestrictionAnalyzer.AnalyzeNode(context.Node))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors[diagnostic.Id],
                diagnostic.Node.GetLocation(),
                diagnostic.Message));
        }
    }
}
