using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            ReportUnsupportedSyntax(node, "Lambda expressions are not supported by Graph C# v0.");
            base.VisitParenthesizedLambdaExpression(node);
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            ReportUnsupportedSyntax(node, "Lambda expressions are not supported by Graph C# v0.");
            base.VisitSimpleLambdaExpression(node);
        }

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            ReportUnsupportedSyntax(node, "Anonymous methods are not supported by Graph C# v0.");
            base.VisitAnonymousMethodExpression(node);
        }

        public override void VisitAwaitExpression(AwaitExpressionSyntax node)
        {
            ReportUnsupportedSyntax(node, "await is not supported by Graph C# v0.");
            base.VisitAwaitExpression(node);
        }

        public override void VisitYieldStatement(YieldStatementSyntax node)
        {
            ReportUnsupportedSyntax(node, "yield is not supported by Graph C# v0.");
            base.VisitYieldStatement(node);
        }

        public override void VisitTryStatement(TryStatementSyntax node)
        {
            ReportUnsupportedSyntax(node, "try/catch/finally is not supported by Graph C# v0.");
            base.VisitTryStatement(node);
        }

        public override void VisitThrowStatement(ThrowStatementSyntax node)
        {
            ReportUnsupportedSyntax(node, "throw is not supported by Graph C# v0.");
            base.VisitThrowStatement(node);
        }

        public override void VisitGotoStatement(GotoStatementSyntax node)
        {
            ReportUnsupportedSyntax(node, "goto is not supported by Graph C# v0.");
            base.VisitGotoStatement(node);
        }

        public override void VisitLockStatement(LockStatementSyntax node)
        {
            ReportUnsupportedSyntax(node, "lock is not supported by Graph C# v0.");
            base.VisitLockStatement(node);
        }

        public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
        {
            ReportUnsupportedSyntax(node, "unsafe blocks are not supported by Graph C# v0.");
            base.VisitUnsafeStatement(node);
        }

        public override void VisitQueryExpression(QueryExpressionSyntax node)
        {
            ReportUnsupportedExpression(node, "LINQ query expressions are not supported by Graph C# v0.");
            base.VisitQueryExpression(node);
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            ReportUnsupportedLoop(node, "for loops are not supported by Graph C# v0.");
            base.VisitForStatement(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            ReportUnsupportedLoop(node, "foreach loops are not supported by Graph C# v0.");
            base.VisitForEachStatement(node);
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            ReportUnsupportedLoop(node, "while loops are not supported by Graph C# v0.");
            base.VisitWhileStatement(node);
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            ReportUnsupportedLoop(node, "do loops are not supported by Graph C# v0.");
            base.VisitDoStatement(node);
        }

        private void ReportUnsupportedSyntax(SyntaxNode node, string message)
        {
            Report(node, "AGC0001", message);
        }

        private void ReportUnsupportedExpression(SyntaxNode node, string message)
        {
            Report(node, "AGC0002", message);
        }

        private void ReportUnsupportedLoop(SyntaxNode node, string message)
        {
            Report(node, "AGC0007", message);
        }

        private void Report(SyntaxNode node, string id, string message)
        {
            var span = node.GetLocation().GetLineSpan();
            var start = span.StartLinePosition;

            diagnostics.Add(new ScriptDiagnostic(
                id,
                "Error",
                message,
                Path.GetFileName(span.Path),
                start.Line + 1,
                start.Character + 1));
        }
    }
}
