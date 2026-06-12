namespace ScriptLab;

internal static class DebugProbeManifestFactory
{
    public static DebugProbeManifest Create(
        string behaviorId,
        string sourcePath,
        BlueprintGraphModule graph)
    {
        var usedProbeIds = new HashSet<int>();
        var probes = new List<DebugProbeSite>();

        foreach (var function in graph.Functions)
        {
            foreach (var node in function.Nodes)
            {
                if (node.Kind is not ("Branch" or "Call" or "Watch"))
                {
                    continue;
                }

                probes.Add(CreateSite(behaviorId, function.Name, node, usedProbeIds));
            }
        }

        return new DebugProbeManifest(behaviorId, sourcePath, probes);
    }

    private static DebugProbeSite CreateSite(
        string behaviorId,
        string functionName,
        BlueprintGraphNode node,
        ISet<int> usedProbeIds)
    {
        var baseKey = string.Join(
            "|",
            behaviorId,
            functionName,
            node.DebugSiteId,
            node.Kind,
            node.Label,
            node.Source.FileName,
            node.Source.Start,
            node.Source.Length);
        var probeId = CreateUniqueProbeId(baseKey, usedProbeIds);

        return new DebugProbeSite(
            probeId,
            node.DebugSiteId,
            node.Id,
            node.Kind,
            node.Label,
            node.Source,
            node.BreakabilityHint,
            node.BreakableVerified,
            CreatePins(node));
    }

    private static int CreateUniqueProbeId(string key, ISet<int> usedProbeIds)
    {
        var suffix = 0;
        while (true)
        {
            var candidate = StablePositiveHash(suffix == 0 ? key : $"{key}|{suffix}");
            if (usedProbeIds.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static int StablePositiveHash(string text)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;

        foreach (var character in text)
        {
            hash ^= character;
            hash *= prime;
        }

        return (int)(hash & 0x7fffffff);
    }

    private static IReadOnlyList<DebugProbePin> CreatePins(BlueprintGraphNode node)
    {
        return node.Kind == "Watch"
            ? new[] { new DebugProbePin(node.Label, "value") }
            : Array.Empty<DebugProbePin>();
    }
}
