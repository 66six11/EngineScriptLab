namespace ScriptLab;

public static class ScriptBreakpointBackendStatus
{
    public const string Applied = "applied";
    public const string Unsupported = "unsupported";
    public const string Unbound = "unbound";
}

public sealed record ScriptBreakpointBackendResult(
    string Key,
    string Status,
    bool Verified,
    string SourcePath,
    int Line,
    int Column,
    string? DebugSiteId,
    string? GraphNodeId,
    int? ProbeId,
    string Message);

public interface IScriptBreakpointBackend
{
    IReadOnlyList<ScriptBreakpointBackendResult> ReplaceSourceBreakpoints(
        string sourcePath,
        IReadOnlyList<ScriptBreakpointState> breakpoints);
}

public sealed class ProbeScriptBreakpointBackend : IScriptBreakpointBackend
{
    private readonly DebugScriptHost host;
    private readonly Dictionary<string, HashSet<string>> debugSiteIdsBySourcePath;

    public ProbeScriptBreakpointBackend(DebugScriptHost host)
    {
        this.host = host;
        debugSiteIdsBySourcePath = new Dictionary<string, HashSet<string>>(GetPathComparer());
    }

    public IReadOnlyList<ScriptBreakpointBackendResult> ReplaceSourceBreakpoints(
        string sourcePath,
        IReadOnlyList<ScriptBreakpointState> breakpoints)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var desiredDebugSiteIds = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<ScriptBreakpointBackendResult>();

        foreach (var breakpoint in breakpoints)
        {
            if (breakpoint.Condition is not null || breakpoint.HitCondition is not null)
            {
                results.Add(CreateResult(
                    breakpoint,
                    ScriptBreakpointBackendStatus.Unsupported,
                    verified: false,
                    probeId: null,
                    "Probe backend does not support conditional or hit-count breakpoints."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(breakpoint.DebugSiteId))
            {
                results.Add(CreateResult(
                    breakpoint,
                    ScriptBreakpointBackendStatus.Unsupported,
                    verified: false,
                    probeId: null,
                    "Probe backend cannot apply source-only breakpoints."));
                continue;
            }

            if (breakpoint.Status is not (ScriptBreakpointBindingStatus.Verified or ScriptBreakpointBindingStatus.Bound))
            {
                results.Add(CreateResult(
                    breakpoint,
                    ScriptBreakpointBackendStatus.Unbound,
                    verified: false,
                    probeId: null,
                    $"Breakpoint binding status '{breakpoint.Status}' is not backend-applicable."));
                continue;
            }

            try
            {
                var site = host.ResolveProbeSiteByDebugSiteId(breakpoint.DebugSiteId);
                desiredDebugSiteIds.Add(breakpoint.DebugSiteId);
                results.Add(CreateResult(
                    breakpoint,
                    ScriptBreakpointBackendStatus.Applied,
                    verified: true,
                    site.ProbeId,
                    "Applied to debug probe backend."));
            }
            catch (InvalidOperationException exception)
            {
                results.Add(CreateResult(
                    breakpoint,
                    ScriptBreakpointBackendStatus.Unsupported,
                    verified: false,
                    probeId: null,
                    exception.Message));
            }
        }

        debugSiteIdsBySourcePath[fullPath] = desiredDebugSiteIds;
        RebuildProbeBreakpoints();
        return results.ToArray();
    }

    private void RebuildProbeBreakpoints()
    {
        host.ClearBreakpoints();

        foreach (var debugSiteId in debugSiteIdsBySourcePath.Values
                     .SelectMany(debugSiteIds => debugSiteIds)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            host.SetBreakpointByDebugSiteId(debugSiteId, enabled: true);
        }
    }

    private static ScriptBreakpointBackendResult CreateResult(
        ScriptBreakpointState breakpoint,
        string status,
        bool verified,
        int? probeId,
        string message)
    {
        return new ScriptBreakpointBackendResult(
            breakpoint.Key,
            status,
            verified,
            breakpoint.SourcePath,
            breakpoint.Line,
            breakpoint.Column,
            breakpoint.DebugSiteId,
            breakpoint.GraphNodeId,
            probeId,
            message);
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }
}
