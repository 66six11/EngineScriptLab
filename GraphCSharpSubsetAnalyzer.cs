using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab.GraphCSharp;

namespace ScriptLab;

public static class GraphCSharpSubsetAnalyzer
{
    public static IReadOnlyList<ScriptDiagnostic> Analyze(CompilationUnitSyntax root)
    {
        var walker = new Walker();
        walker.Visit(root);
        return walker.Diagnostics;
    }

    private sealed class Walker : CSharpSyntaxWalker
    {
        private readonly List<ScriptDiagnostic> diagnostics = new();

        public IReadOnlyList<ScriptDiagnostic> Diagnostics => diagnostics;

        public override void Visit(SyntaxNode? node)
        {
            if (node is null)
            {
                return;
            }

            foreach (var diagnostic in GraphCSharpRestrictionAnalyzer.AnalyzeNode(node))
            {
                Report(diagnostic);
            }

            base.Visit(node);
        }

        private void Report(GraphCSharpRestrictionDiagnostic diagnostic)
        {
            var span = diagnostic.Node.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;

            diagnostics.Add(new ScriptDiagnostic(
                diagnostic.Id,
                GraphCSharpRuleSet.ErrorSeverity,
                diagnostic.Message,
                Path.GetFileName(span.Path),
                start.Line + 1,
                start.Character + 1));
        }
    }
}
