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

            var rule = GraphCSharpRuleSet.Find(node.Kind());
            if (rule is not null)
            {
                Report(node, rule);
            }

            if (node is InvocationExpressionSyntax invocation)
            {
                ReportUnregisteredFunctionCall(invocation);
            }

            base.Visit(node);
        }

        private void ReportUnregisteredFunctionCall(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return;
            }

            var csharpName = memberAccess.ToString();
            if (GraphCSharpBindingRegistry.TryGetFunctionId(csharpName, out _))
            {
                return;
            }

            Report(
                invocation,
                "AGC0003",
                GraphCSharpBindingRegistry.GetUnregisteredFunctionCallMessage(csharpName));
        }

        private void Report(SyntaxNode node, GraphCSharpSyntaxRule rule)
        {
            Report(node, rule.Id, rule.Message);
        }

        private void Report(SyntaxNode node, string id, string message)
        {
            var span = node.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;

            diagnostics.Add(new ScriptDiagnostic(
                id,
                GraphCSharpRuleSet.ErrorSeverity,
                message,
                Path.GetFileName(span.Path),
                start.Line + 1,
                start.Character + 1));
        }
    }
}
