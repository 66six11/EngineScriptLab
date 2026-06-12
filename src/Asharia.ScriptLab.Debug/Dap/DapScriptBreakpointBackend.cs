using System.Text.Json.Nodes;

using ScriptLab;

namespace ScriptLab.Debug.Dap;

public sealed record DapBreakpointBackendCapabilities(
    bool SupportsConditionalBreakpoints = false,
    bool SupportsHitConditionalBreakpoints = false,
    bool SupportsBreakpointLocationsRequest = false,
    bool SupportsInstructionBreakpoints = false)
{
    public static DapBreakpointBackendCapabilities FromInitializeResponseBody(JsonObject body)
    {
        return new DapBreakpointBackendCapabilities(
            GetBoolean(body, "supportsConditionalBreakpoints"),
            GetBoolean(body, "supportsHitConditionalBreakpoints"),
            GetBoolean(body, "supportsBreakpointLocationsRequest"),
            GetBoolean(body, "supportsInstructionBreakpoints"));
    }

    private static bool GetBoolean(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node) &&
               node is not null &&
               node.GetValue<bool>();
    }
}

public sealed class DapScriptBreakpointBackend : IScriptBreakpointBackend
{
    private readonly DapDebugSessionClient client;
    private readonly DapBreakpointBackendCapabilities capabilities;

    public DapScriptBreakpointBackend(
        IDapRequestClient client,
        DapBreakpointBackendCapabilities? capabilities = null)
        : this(new DapDebugSessionClient(client), capabilities)
    {
    }

    public DapScriptBreakpointBackend(
        DapDebugSessionClient client,
        DapBreakpointBackendCapabilities? capabilities = null)
    {
        this.client = client;
        this.capabilities = capabilities ?? new DapBreakpointBackendCapabilities();
    }

    public IReadOnlyList<ScriptBreakpointBackendResult> ReplaceSourceBreakpoints(
        string sourcePath,
        IReadOnlyList<ScriptBreakpointState> breakpoints)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        var results = new ScriptBreakpointBackendResult?[breakpoints.Count];
        var sentBreakpoints = new List<(int OriginalIndex, ScriptBreakpointState Breakpoint)>();
        var requestBreakpoints = new List<DapSourceBreakpointRequest>();

        for (var index = 0; index < breakpoints.Count; index++)
        {
            var breakpoint = breakpoints[index];
            var unsupportedReason = GetUnsupportedReason(breakpoint);
            if (unsupportedReason is not null)
            {
                results[index] = CreateResult(
                    breakpoint,
                    ScriptBreakpointBackendStatus.Unsupported,
                    verified: false,
                    unsupportedReason);
                continue;
            }

            sentBreakpoints.Add((index, breakpoint));
            requestBreakpoints.Add(new DapSourceBreakpointRequest(
                breakpoint.Line,
                breakpoint.Column,
                breakpoint.Condition,
                breakpoint.HitCondition));
        }

        var responseBreakpoints = client.SetBreakpoints(fullPath, requestBreakpoints);
        for (var responseIndex = 0; responseIndex < sentBreakpoints.Count; responseIndex++)
        {
            var sent = sentBreakpoints[responseIndex];
            if (responseIndex >= responseBreakpoints.Count)
            {
                results[sent.OriginalIndex] = CreateResult(
                    sent.Breakpoint,
                    ScriptBreakpointBackendStatus.Unbound,
                    verified: false,
                    "DAP adapter returned fewer breakpoints than requested.");
                continue;
            }

            var responseBreakpoint = responseBreakpoints[responseIndex];
            var verified = responseBreakpoint.Verified;
            results[sent.OriginalIndex] = CreateResult(
                sent.Breakpoint,
                verified ? ScriptBreakpointBackendStatus.Applied : ScriptBreakpointBackendStatus.Unbound,
                verified,
                responseBreakpoint.Message ??
                (verified
                    ? "Applied to DAP adapter."
                    : "DAP adapter returned an unverified breakpoint."));
        }

        return results
            .Select((result, index) => result ?? CreateResult(
                breakpoints[index],
                ScriptBreakpointBackendStatus.Unbound,
                verified: false,
                "DAP backend did not produce a result for this breakpoint."))
            .ToArray();
    }

    private string? GetUnsupportedReason(ScriptBreakpointState breakpoint)
    {
        if (!string.IsNullOrWhiteSpace(breakpoint.Condition) &&
            !capabilities.SupportsConditionalBreakpoints)
        {
            return "DAP adapter capability 'supportsConditionalBreakpoints' is not enabled.";
        }

        if (!string.IsNullOrWhiteSpace(breakpoint.HitCondition) &&
            !capabilities.SupportsHitConditionalBreakpoints)
        {
            return "DAP adapter capability 'supportsHitConditionalBreakpoints' is not enabled.";
        }

        return null;
    }

    private static ScriptBreakpointBackendResult CreateResult(
        ScriptBreakpointState breakpoint,
        string status,
        bool verified,
        string message)
    {
        return new ScriptBreakpointBackendResult(
            breakpoint.Key,
            status,
            verified,
            Synthetic: false,
            breakpoint.SourcePath,
            breakpoint.Line,
            breakpoint.Column,
            breakpoint.DebugSiteId,
            breakpoint.GraphNodeId,
            ProbeId: null,
            message);
    }
}
