using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ScriptLab;

public sealed record GraphEditResult(bool Applied, string Source, string? Message)
{
    public static GraphEditResult Success(string source)
    {
        return new GraphEditResult(true, source, null);
    }

    public static GraphEditResult Failure(string source, string message)
    {
        return new GraphEditResult(false, source, message);
    }
}

public static class GraphSourceRewriter
{
    public static GraphEditResult ReplaceLiteral(
        string source,
        GraphNodeIdentity literalNode,
        string newLiteral)
    {
        if (!TryValidateAnchor(source, literalNode, out var failure))
        {
            return failure;
        }

        var root = ParseRoot(source);
        if (!TryFindExactNode<LiteralExpressionSyntax>(root, literalNode.SourceAnchor, out var literal))
        {
            return GraphEditResult.Failure(source, "Could not find a literal expression at the requested source anchor.");
        }

        var replacement = SyntaxFactory.ParseExpression(newLiteral);
        if (replacement.ContainsDiagnostics || replacement is not LiteralExpressionSyntax)
        {
            return GraphEditResult.Failure(source, $"'{newLiteral}' is not a valid C# literal expression.");
        }

        var updatedRoot = root.ReplaceNode(literal, replacement.WithTriviaFrom(literal));
        return GraphEditResult.Success(updatedRoot.ToFullString());
    }

    public static GraphEditResult ReplaceEnumMember(
        string source,
        GraphNodeIdentity enumNode,
        string newEnumText)
    {
        if (!TryValidateAnchor(source, enumNode, out var failure))
        {
            return failure;
        }

        var root = ParseRoot(source);
        if (!TryFindExactNode<MemberAccessExpressionSyntax>(root, enumNode.SourceAnchor, out var enumMember))
        {
            return GraphEditResult.Failure(source, "Could not find an enum member expression at the requested source anchor.");
        }

        var replacement = SyntaxFactory.ParseExpression(newEnumText);
        if (replacement.ContainsDiagnostics || replacement is not MemberAccessExpressionSyntax)
        {
            return GraphEditResult.Failure(source, $"'{newEnumText}' is not a valid enum member expression.");
        }

        var updatedRoot = root.ReplaceNode(enumMember, replacement.WithTriviaFrom(enumMember));
        return GraphEditResult.Success(updatedRoot.ToFullString());
    }

    public static GraphEditResult InsertStatementAfter(
        string source,
        GraphNodeIdentity anchorNode,
        string statementText)
    {
        if (!TryValidateAnchor(source, anchorNode, out var failure))
        {
            return failure;
        }

        var root = ParseRoot(source);
        if (!TryFindContainingStatement(root, anchorNode.SourceAnchor, out var anchorStatement, out var block))
        {
            return GraphEditResult.Failure(source, "Could not find a statement containing the requested source anchor.");
        }

        var inserted = SyntaxFactory.ParseStatement(statementText);
        if (inserted.ContainsDiagnostics)
        {
            return GraphEditResult.Failure(source, $"'{statementText}' is not a valid C# statement.");
        }

        var anchorIndex = block.Statements.IndexOf(anchorStatement);
        var updatedStatements = block.Statements.Insert(
            anchorIndex + 1,
            inserted
                .WithLeadingTrivia(anchorStatement.GetLeadingTrivia())
                .WithTrailingTrivia(anchorStatement.GetTrailingTrivia()));
        var updatedRoot = root.ReplaceNode(block, block.WithStatements(updatedStatements));
        return GraphEditResult.Success(updatedRoot.ToFullString());
    }

    public static GraphEditResult ReorderStatements(
        string source,
        GraphNodeIdentity first,
        GraphNodeIdentity second)
    {
        if (!TryValidateAnchor(source, first, out var firstFailure))
        {
            return firstFailure;
        }

        if (!TryValidateAnchor(source, second, out var secondFailure))
        {
            return secondFailure;
        }

        var root = ParseRoot(source);
        if (!TryFindContainingStatement(root, first.SourceAnchor, out var firstStatement, out var firstBlock) ||
            !TryFindContainingStatement(root, second.SourceAnchor, out var secondStatement, out var secondBlock))
        {
            return GraphEditResult.Failure(source, "Could not find both statements for reorder.");
        }

        if (!firstBlock.Span.Equals(secondBlock.Span))
        {
            return GraphEditResult.Failure(source, "Statements must be in the same block to reorder.");
        }

        var firstIndex = firstBlock.Statements.IndexOf(firstStatement);
        var secondIndex = firstBlock.Statements.IndexOf(secondStatement);
        if (firstIndex == secondIndex)
        {
            return GraphEditResult.Failure(source, "Cannot reorder a statement with itself.");
        }

        var reordered = firstBlock.Statements.ToArray();
        reordered[firstIndex] = secondStatement.WithTriviaFrom(firstStatement);
        reordered[secondIndex] = firstStatement.WithTriviaFrom(secondStatement);
        var updatedRoot = root.ReplaceNode(firstBlock, firstBlock.WithStatements(SyntaxFactory.List(reordered)));
        return GraphEditResult.Success(updatedRoot.ToFullString());
    }

    private static CompilationUnitSyntax ParseRoot(string source)
    {
        return CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
    }

    private static bool TryValidateAnchor(
        string source,
        GraphNodeIdentity identity,
        out GraphEditResult failure)
    {
        failure = GraphEditResult.Success(source);

        if (identity.SourceAnchor.Start < 0 ||
            identity.SourceAnchor.Length < 0 ||
            identity.SourceAnchor.End > source.Length)
        {
            failure = GraphEditResult.Failure(source, "The graph node source anchor is outside the source text.");
            return false;
        }

        if (identity.SourceTextHash.Length > 0)
        {
            var currentHash = GraphNodeIdentity.ComputeSourceTextHash(
                source,
                identity.SourceAnchor.Start,
                identity.SourceAnchor.Length);
            if (!string.Equals(currentHash, identity.SourceTextHash, StringComparison.Ordinal))
            {
                failure = GraphEditResult.Failure(source, "The source text at the graph node anchor has changed.");
                return false;
            }
        }

        return true;
    }

    private static bool TryFindExactNode<TNode>(
        SyntaxNode root,
        SourceAnchor anchor,
        out TNode node)
        where TNode : SyntaxNode
    {
        node = root
            .DescendantNodes()
            .OfType<TNode>()
            .FirstOrDefault(candidate =>
                candidate.Span.Start == anchor.Start &&
                candidate.Span.Length == anchor.Length)!;
        return node is not null;
    }

    private static bool TryFindContainingStatement(
        SyntaxNode root,
        SourceAnchor anchor,
        out StatementSyntax statement,
        out BlockSyntax block)
    {
        var span = new TextSpan(anchor.Start, anchor.Length);
        statement = root
            .DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(candidate => candidate.Span.Contains(span))
            .OrderBy(candidate => candidate.Span.Length)
            .FirstOrDefault()!;
        block = statement?.Parent as BlockSyntax ?? null!;
        return statement is not null && block is not null;
    }
}
