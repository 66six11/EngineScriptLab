using System.Text.Json.Nodes;

namespace ScriptLab;

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

public interface IDapRequestClient
{
    JsonObject SendRequest(string command, JsonObject arguments);
}

public sealed class DapScriptBreakpointBackend : IScriptBreakpointBackend
{
    private readonly IDapRequestClient client;
    private readonly DapBreakpointBackendCapabilities capabilities;

    public DapScriptBreakpointBackend(
        IDapRequestClient client,
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
        var requestBreakpoints = new JsonArray();

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
            requestBreakpoints.Add(CreateDapSourceBreakpoint(breakpoint));
        }

        var responseBody = client.SendRequest(
            "setBreakpoints",
            new JsonObject
            {
                ["source"] = new JsonObject
                {
                    ["path"] = fullPath
                },
                ["breakpoints"] = requestBreakpoints,
                ["sourceModified"] = false
            });

        var responseBreakpoints = GetResponseBreakpoints(responseBody);
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

            var responseBreakpoint = responseBreakpoints[responseIndex]!.AsObject();
            var verified = GetBoolean(responseBreakpoint, "verified");
            results[sent.OriginalIndex] = CreateResult(
                sent.Breakpoint,
                verified ? ScriptBreakpointBackendStatus.Applied : ScriptBreakpointBackendStatus.Unbound,
                verified,
                GetString(responseBreakpoint, "message") ??
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

    private static JsonObject CreateDapSourceBreakpoint(ScriptBreakpointState breakpoint)
    {
        var sourceBreakpoint = new JsonObject
        {
            ["line"] = breakpoint.Line,
            ["column"] = breakpoint.Column
        };

        if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
        {
            sourceBreakpoint["condition"] = breakpoint.Condition;
        }

        if (!string.IsNullOrWhiteSpace(breakpoint.HitCondition))
        {
            sourceBreakpoint["hitCondition"] = breakpoint.HitCondition;
        }

        return sourceBreakpoint;
    }

    private static IReadOnlyList<JsonNode?> GetResponseBreakpoints(JsonObject responseBody)
    {
        return responseBody.TryGetPropertyValue("breakpoints", out var breakpointsNode) &&
               breakpointsNode is JsonArray breakpoints
            ? breakpoints.ToArray()
            : Array.Empty<JsonNode?>();
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

    private static bool GetBoolean(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node) &&
               node is not null &&
               node.GetValue<bool>();
    }

    private static string? GetString(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out var node)
            ? node?.GetValue<string>()
            : null;
    }
}
