using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab.GraphCSharp;

namespace ScriptLab;

public static class GraphCSharpSubsetAnalyzer
{
    public static IReadOnlyList<ScriptDiagnostic> Analyze(SyntaxTree tree)
    {
        var root = tree.GetCompilationUnitRoot();
        var walker = new Walker(GraphCSharpSemanticModelFactory.CreateSemanticModel(tree));
        walker.Visit(root);
        walker.ReportSourceMapDirectives(root);
        return walker.Diagnostics;
    }

    private sealed class Walker : CSharpSyntaxWalker
    {
        private readonly SemanticModel semanticModel;
        private readonly List<ScriptDiagnostic> diagnostics = new();

        public Walker(SemanticModel semanticModel)
        {
            this.semanticModel = semanticModel;
        }

        public IReadOnlyList<ScriptDiagnostic> Diagnostics => diagnostics;

        public void ReportSourceMapDirectives(SyntaxNode root)
        {
            foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                if (trivia.IsKind(SyntaxKind.LineDirectiveTrivia) ||
                    trivia.IsKind(SyntaxKind.LineSpanDirectiveTrivia))
                {
                    Report(
                        trivia.GetLocation(),
                        GraphCSharpRuleSet.SourceMapUnavailableId,
                        GraphCSharpAnalysisStage.SourceMapInvariant,
                        GraphCSharpRuleSet.GetSourceMapUnavailableMessage(
                            "#line directives are not supported because graph nodes must map to the original script source."));
                }
            }
        }

        public override void Visit(SyntaxNode? node)
        {
            if (node is null)
            {
                return;
            }

            foreach (var diagnostic in GraphCSharpRestrictionAnalyzer.AnalyzeNode(node, semanticModel))
            {
                Report(diagnostic);
            }

            base.Visit(node);
        }

        private void Report(GraphCSharpRestrictionDiagnostic diagnostic)
        {
            Report(
                diagnostic.Node.GetLocation(),
                diagnostic.Id,
                diagnostic.Stage,
                diagnostic.Message);
        }

        private void Report(Location location, string id, string stage, string message)
        {
            var span = location.GetLineSpan();
            var start = span.StartLinePosition;

            diagnostics.Add(new ScriptDiagnostic(
                id,
                stage,
                GraphCSharpRuleSet.ErrorSeverity,
                message,
                Path.GetFileName(span.Path),
                start.Line + 1,
                start.Character + 1));
        }
    }
}
