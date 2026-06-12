namespace ScriptLab;

public static class GraphNodeIdentityFactory
{
    public static GraphNodeIdentity FromNode(
        string source,
        string functionName,
        BlueprintGraphNode node,
        IReadOnlyList<BlueprintGraphNode> siblings)
    {
        return new GraphNodeIdentity(
            functionName,
            node.Kind,
            GetBoundId(node),
            new SourceAnchor(node.Source.FileName, node.Source.Start, node.Source.Length),
            GetOrdinalAmongSiblings(node, siblings),
            GraphNodeIdentity.ComputeSourceTextHash(source, node.Source.Start, node.Source.Length));
    }

    private static int GetOrdinalAmongSiblings(
        BlueprintGraphNode node,
        IReadOnlyList<BlueprintGraphNode> siblings)
    {
        var boundId = GetBoundId(node);
        var ordinal = 0;

        foreach (var sibling in siblings)
        {
            if (ReferenceEquals(sibling, node))
            {
                return ordinal;
            }

            if (sibling.Kind == node.Kind &&
                string.Equals(GetBoundId(sibling), boundId, StringComparison.Ordinal))
            {
                ordinal++;
            }
        }

        return ordinal;
    }

    private static string? GetBoundId(BlueprintGraphNode node)
    {
        return node.Kind switch
        {
            "Call" => node.Label,
            "Enum" => node.Label,
            "GetField" => node.Label.Split(' ', 2)[0],
            "MakeStruct" => node.Label,
            _ => null
        };
    }
}
