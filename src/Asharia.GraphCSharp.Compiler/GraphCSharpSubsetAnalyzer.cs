using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab.GraphCSharp;

namespace ScriptLab;

public static class GraphCSharpSubsetAnalyzer
{
    private static readonly string[] NodeAnalysisStages =
    {
        GraphCSharpAnalysisStage.SyntaxRestriction,
        GraphCSharpAnalysisStage.SemanticBinding,
        GraphCSharpAnalysisStage.TypeCheck,
        GraphCSharpAnalysisStage.EffectCheck,
        GraphCSharpAnalysisStage.ContextCheck
    };

    public static IReadOnlyList<ScriptDiagnostic> Analyze(SyntaxTree tree)
    {
        var root = tree.GetCompilationUnitRoot();
        var semanticModel = GraphCSharpSemanticModelFactory.CreateSemanticModel(tree);
        var diagnostics = new List<ScriptDiagnostic>();

        foreach (var stage in NodeAnalysisStages)
        {
            var walker = new NodeStageWalker(semanticModel, stage);
            walker.Visit(root);
            diagnostics.AddRange(walker.Diagnostics);
        }

        diagnostics.AddRange(SourceMapInvariantPass.Analyze(root));
        return diagnostics;
    }

    private sealed class NodeStageWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel semanticModel;
        private readonly string stage;
        private readonly List<ScriptDiagnostic> diagnostics = new();

        public NodeStageWalker(SemanticModel semanticModel, string stage)
        {
            this.semanticModel = semanticModel;
            this.stage = stage;
        }

        public IReadOnlyList<ScriptDiagnostic> Diagnostics => diagnostics;

        public override void Visit(SyntaxNode? node)
        {
            if (node is null)
            {
                return;
            }

            foreach (var diagnostic in GraphCSharpRestrictionAnalyzer.AnalyzeNode(node, semanticModel))
            {
                if (diagnostic.Stage == stage)
                {
                    diagnostics.Add(CreateDiagnostic(
                        diagnostic.Node.GetLocation(),
                        diagnostic.Id,
                        diagnostic.Stage,
                        diagnostic.Message));
                }
            }

            base.Visit(node);
        }
    }

    private static class SourceMapInvariantPass
    {
        public static IReadOnlyList<ScriptDiagnostic> Analyze(SyntaxNode root)
        {
            var diagnostics = new List<ScriptDiagnostic>();
            foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                if (trivia.IsKind(SyntaxKind.LineDirectiveTrivia) ||
                    trivia.IsKind(SyntaxKind.LineSpanDirectiveTrivia))
                {
                    diagnostics.Add(CreateDiagnostic(
                        trivia.GetLocation(),
                        GraphCSharpRuleSet.SourceMapUnavailableId,
                        GraphCSharpAnalysisStage.SourceMapInvariant,
                        GraphCSharpRuleSet.GetSourceMapUnavailableMessage(
                            "#line directives are not supported because graph nodes must map to the original script source.")));
                }
            }

            return diagnostics;
        }
    }

    private static ScriptDiagnostic CreateDiagnostic(Location location, string id, string stage, string message)
    {
        var span = location.GetLineSpan();
        var start = span.StartLinePosition;

        return new ScriptDiagnostic(
            id,
            stage,
            GraphCSharpRuleSet.ErrorSeverity,
            message,
            Path.GetFileName(span.Path),
            start.Line + 1,
            start.Character + 1);
    }
}
