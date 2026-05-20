using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ScriptLab;

internal sealed class DebugInstrumentationRewriter : CSharpSyntaxRewriter
{
    private readonly DebugProbeManifest manifest;
    private readonly List<DebugProbeSite> probeSites = new();

    public DebugInstrumentationRewriter(DebugProbeManifest manifest)
    {
        this.manifest = manifest;
    }

    public IReadOnlyList<DebugProbeSite> ProbeSites => probeSites;

    public override SyntaxNode? VisitBlock(BlockSyntax node)
    {
        var rewrittenStatements = new List<StatementSyntax>();

        foreach (var statement in node.Statements)
        {
            DebugProbeSite? site = null;
            if (TryCreateEnterProbe(statement, out var probe, out site))
            {
                rewrittenStatements.Add(probe);
            }

            var rewrittenStatement = (StatementSyntax)Visit(statement)!;
            if (site is not null)
            {
                rewrittenStatement = AddSourceLineDirective(rewrittenStatement, statement, site);
            }

            rewrittenStatements.Add(rewrittenStatement);
        }

        return node.WithStatements(SyntaxFactory.List(rewrittenStatements));
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (TryRewriteGraphDebugInvocation(node, out var rewritten))
        {
            return rewritten;
        }

        return base.VisitInvocationExpression(node);
    }

    private bool TryRewriteGraphDebugInvocation(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax rewritten)
    {
        rewritten = null!;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Expression.ToString() != "GraphDebug" ||
            memberAccess.Name.Identifier.ValueText is not ("Inspect" or "Watch"))
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 2)
        {
            return false;
        }

        var watchProbe = FindProbe("Watch", invocation.Span);
        if (watchProbe is null)
        {
            return false;
        }

        var pinId = GetDebugWatchName(arguments[0].Expression);
        var value = (ExpressionSyntax)Visit(arguments[1].Expression)!;
        AddProbeSite(watchProbe);
        rewritten = SyntaxFactory.ParseExpression(
                $"global::Asharia.Behavior.DebugProbe.Value({watchProbe.ProbeId}, \"{EscapeString(pinId)}\", {value})")
            .WithTriviaFrom(invocation);
        return true;
    }

    private bool TryCreateEnterProbe(
        StatementSyntax statement,
        out StatementSyntax probe,
        out DebugProbeSite? site)
    {
        site = FindEnterProbe(statement);
        if (site is null)
        {
            probe = null!;
            return false;
        }

        AddProbeSite(site);
        probe = SyntaxFactory
            .ParseStatement($"global::Asharia.Behavior.DebugProbe.Enter({site.ProbeId});")
            .WithLeadingTrivia(statement.GetLeadingTrivia()
                .AddRange(SyntaxFactory.ParseLeadingTrivia($"#line hidden{Environment.NewLine}"))
                .AddRange(GetIndentationTrivia(statement.GetLeadingTrivia())))
            .WithTrailingTrivia(SyntaxFactory.ParseTrailingTrivia(
                Environment.NewLine));
        return true;
    }

    private StatementSyntax AddSourceLineDirective(
        StatementSyntax rewrittenStatement,
        StatementSyntax originalStatement,
        DebugProbeSite site)
    {
        var sourcePath = EscapeLineDirectivePath(manifest.SourcePath);
        var originalLeadingTrivia = originalStatement.GetLeadingTrivia();
        return rewrittenStatement.WithLeadingTrivia(originalLeadingTrivia
            .AddRange(SyntaxFactory.ParseLeadingTrivia($"#line {site.Source.Line} \"{sourcePath}\"{Environment.NewLine}"))
            .AddRange(GetIndentationTrivia(originalLeadingTrivia)));
    }

    private DebugProbeSite? FindEnterProbe(StatementSyntax statement)
    {
        return statement switch
        {
            IfStatementSyntax ifStatement => FindProbe("Branch", ifStatement.Span),
            ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } =>
                FindProbe("Call", invocation.Span),
            _ => null
        };
    }

    private DebugProbeSite? FindProbe(string kind, TextSpan span)
    {
        foreach (var probe in manifest.Probes)
        {
            if (probe.Kind == kind &&
                probe.Source.Start == span.Start &&
                probe.Source.Length == span.Length)
            {
                return probe;
            }
        }

        return null;
    }

    private void AddProbeSite(DebugProbeSite site)
    {
        if (probeSites.All(existing => existing.ProbeId != site.ProbeId))
        {
            probeSites.Add(site);
        }
    }

    private static string GetDebugWatchName(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression)
                ? literal.Token.ValueText
                : expression.ToString();
    }

    private static string EscapeString(string text)
    {
        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static SyntaxTriviaList GetIndentationTrivia(SyntaxTriviaList trivia)
    {
        var indentation = trivia
            .Reverse()
            .TakeWhile(item => item.IsKind(SyntaxKind.WhitespaceTrivia))
            .Reverse();
        return SyntaxFactory.TriviaList(indentation);
    }

    private static string EscapeLineDirectivePath(string path)
    {
        return Path.GetFullPath(path)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
