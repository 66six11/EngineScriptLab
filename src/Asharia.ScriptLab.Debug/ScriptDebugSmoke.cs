using System.Globalization;
using System.Text.Json.Serialization;

namespace ScriptLab;

public sealed record ScriptDebugSmokeOptions(
    string? GraphNodeId = null,
    int EntityId = 1,
    float Delta = 0.016f,
    bool PressKeyW = true,
    bool ObserveTrace = true,
    bool ObserveWatch = true);

public sealed record ScriptDebugSmokeCheck(
    string Name,
    string Status,
    string Message);

public static class ScriptDebugSmokeCheckStatus
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
}

public sealed record ScriptDebugSmokeResult(
    string ScriptPath,
    string OutputDirectory,
    string BehaviorId,
    string? GraphNodeId,
    string? DebugSiteId,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<ScriptDebugSmokeCheck> Checks)
{
    [JsonIgnore]
    public bool Success => Failed == 0;
}

public static class ScriptDebugSmoke
{
    public static ScriptDebugSmokeResult RunFile(
        string scriptPath,
        string outputDirectory,
        ScriptDebugSmokeOptions? options = null)
    {
        options ??= new ScriptDebugSmokeOptions();

        var checks = new List<ScriptDebugSmokeCheck>();
        var fullPath = Path.GetFullPath(scriptPath);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        var emit = SourceInstrumentedDebugCompiler.EmitFile(fullPath, fullOutputDirectory);
        var sourceText = File.ReadAllText(emit.DebugMap.SourceDocumentPath);
        var session = new DebugSessionCore(emit.DebugMap, sourceText, emit.DebugMap.SourceDocumentPath);
        var host = DebugScriptHost.Load(emit);
        var backend = new ProbeScriptBreakpointBackend(host);
        session.SetTraceObservationEnabled(options.ObserveTrace);
        session.SetWatchObservationEnabled(options.ObserveWatch);
        host.SetTraceEnabled(options.ObserveTrace);
        host.SetWatchEnabled(options.ObserveWatch);

        AddCheck(
            checks,
            "debug-map-output",
            File.Exists(emit.AssemblyPath) &&
            File.Exists(emit.PdbPath) &&
            File.Exists(emit.DebugMapPath) &&
            emit.DebugMap.Functions.SelectMany(function => function.Sites).Any(),
            $"assembly={Path.GetFileName(emit.AssemblyPath)} pdb={Path.GetFileName(emit.PdbPath)} map={Path.GetFileName(emit.DebugMapPath)}");

        var targetSite = ResolveTargetSite(emit.DebugMap, options.GraphNodeId);
        if (targetSite is null)
        {
            AddCheck(
                checks,
                "target-site",
                passed: false,
                "No breakable debug site with a probe was found.");
            return CreateResult(fullPath, fullOutputDirectory, emit.DebugMap.BehaviorId, null, null, checks);
        }

        AddCheck(
            checks,
            "target-site",
            passed: true,
            $"{targetSite.Kind} {targetSite.Label} graph={targetSite.GraphNodeId} site={targetSite.DebugSiteId}");

        var blueprintBinding = session.ResolveBlueprintBreakpoint(targetSite.GraphNodeId);
        AddCheck(
            checks,
            "blueprint-breakpoint-resolve",
            blueprintBinding.Status is ScriptBreakpointBindingStatus.Verified or ScriptBreakpointBindingStatus.Bound &&
            blueprintBinding.DebugSiteId == targetSite.DebugSiteId,
            $"status={blueprintBinding.Status} site={blueprintBinding.DebugSiteId ?? "<none>"}");

        if (targetSite.PdbSequencePoint is null)
        {
            AddSkipped(
                checks,
                "source-breakpoint-resolve",
                "Target site has no PDB sequence point.");
        }
        else
        {
            var sourceBinding = session.ResolveSourceBreakpoint(
                targetSite.PdbSequencePoint.DocumentPath,
                targetSite.PdbSequencePoint.StartLine,
                targetSite.PdbSequencePoint.StartColumn);
            AddCheck(
                checks,
                "source-breakpoint-resolve",
                sourceBinding.Status is ScriptBreakpointBindingStatus.Verified or ScriptBreakpointBindingStatus.Bound &&
                sourceBinding.DebugSiteId == targetSite.DebugSiteId,
                $"status={sourceBinding.Status} source={targetSite.PdbSequencePoint.StartLine}:{targetSite.PdbSequencePoint.StartColumn}");
        }

        var branchSite = FindSite(emit.DebugMap, "Branch");
        if (branchSite is null ||
            !TryFindOpenBrace(sourceText, branchSite.SourceSpan.Line, out var braceLine, out var braceColumn))
        {
            AddSkipped(
                checks,
                "source-open-brace-resolve",
                "No branch open brace position was found in the sample.");
        }
        else
        {
            var braceBinding = session.ResolveSourceBreakpoint(
                emit.DebugMap.SourceDocumentPath,
                braceLine,
                braceColumn);
            AddCheck(
                checks,
                "source-open-brace-resolve",
                braceBinding.Status == ScriptBreakpointBindingStatus.Verified &&
                braceBinding.DebugSiteId == branchSite.DebugSiteId,
                $"status={braceBinding.Status} source={braceLine}:{braceColumn} site={braceBinding.DebugSiteId ?? "<none>"}");
        }

        var breakpoints = session.SetBlueprintBreakpoints(new[] { targetSite.GraphNodeId });
        var backendResults = session.ApplySourceBreakpoints(backend, emit.DebugMap.SourceDocumentPath);
        var appliedBackend = backendResults.FirstOrDefault(result => result.DebugSiteId == targetSite.DebugSiteId);
        AddCheck(
            checks,
            "backend-apply",
            appliedBackend is { Status: ScriptBreakpointBackendStatus.Applied, Verified: false, Synthetic: true },
            appliedBackend is null
                ? "No backend result for target site."
                : $"status={appliedBackend.Status} verified={appliedBackend.Verified.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} synthetic={appliedBackend.Synthetic.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");

        var instance = host.MountBehavior(options.EntityId, emit.DebugMap.BehaviorId);
        host.ClearInput();
        if (options.PressKeyW)
        {
            host.SetInputKeyDown("W", isDown: true);
        }

        session.ResetProbeEventCursor();
        session.ClearWatchValues();
        session.ClearTrace();
        host.ClearProbeEvents();
        instance.InvokeUpdate(options.Delta);

        var probeEvents = host.GetProbeEvents();
        var ingest = session.IngestProbeEvents(probeEvents);
        var stoppedEvent = ingest.StoppedEvents.LastOrDefault();
        AddCheck(
            checks,
            "breakpoint-hit",
            stoppedEvent is
            {
                Status: ScriptStoppedEventStatus.Resolved,
                DebugSiteId: not null
            } &&
            stoppedEvent.DebugSiteId == targetSite.DebugSiteId,
            stoppedEvent is null
                ? "No stopped event was produced."
                : $"status={stoppedEvent.Status} reason={stoppedEvent.Reason} site={stoppedEvent.DebugSiteId ?? "<none>"}");

        var snapshot = stoppedEvent is null
            ? null
            : session.ReadPausedSnapshot(host, stoppedEvent, options.EntityId);
        var inspector = snapshot?.Scopes.FirstOrDefault(scope => scope.Kind == ScriptDebugScopeKind.Inspector);
        AddCheck(
            checks,
            "paused-inspector",
            snapshot is { Status: ScriptPausedSnapshotStatus.Partial } &&
            inspector is { Available: true } &&
            inspector.Variables.Count > 0,
            inspector is null
                ? "Inspector scope is unavailable."
                : $"variables={inspector.Variables.Count.ToString(CultureInfo.InvariantCulture)}");

        var traceSite = ingest.TraceSnapshot.Sites.FirstOrDefault(site => site.DebugSiteId == targetSite.DebugSiteId);
        AddCheck(
            checks,
            "trace-aggregation",
            traceSite is { HitCount: > 0 },
            traceSite is null
                ? "Target site was not present in trace snapshot."
                : $"hits={traceSite.HitCount.ToString(CultureInfo.InvariantCulture)} last={traceSite.LastSequence.ToString(CultureInfo.InvariantCulture)}");

        return CreateResult(
            fullPath,
            fullOutputDirectory,
            emit.DebugMap.BehaviorId,
            targetSite.GraphNodeId,
            targetSite.DebugSiteId,
            checks);
    }

    private static ScriptDebugMapSite? ResolveTargetSite(ScriptDebugMap debugMap, string? graphNodeId)
    {
        var sites = debugMap.Functions.SelectMany(function => function.Sites).ToArray();
        if (!string.IsNullOrWhiteSpace(graphNodeId))
        {
            return sites.FirstOrDefault(site => site.GraphNodeId == graphNodeId);
        }

        return FindSite(debugMap, "Branch")
            ?? sites.FirstOrDefault(site => site.BreakableVerified && site.ProbeId is not null)
            ?? sites.FirstOrDefault(site => site.ProbeId is not null);
    }

    private static ScriptDebugMapSite? FindSite(ScriptDebugMap debugMap, string kind)
    {
        return debugMap.Functions
            .SelectMany(function => function.Sites)
            .FirstOrDefault(site =>
                string.Equals(site.Kind, kind, StringComparison.Ordinal) &&
                site.BreakableVerified &&
                site.ProbeId is not null);
    }

    private static bool TryFindOpenBrace(
        string sourceText,
        int oneBasedStartLine,
        out int line,
        out int column)
    {
        var lines = sourceText.Replace("\r\n", "\n").Split('\n');
        var start = Math.Max(0, oneBasedStartLine - 1);
        var end = Math.Min(lines.Length, start + 6);

        for (var index = start; index < end; index++)
        {
            var braceIndex = lines[index].IndexOf('{', StringComparison.Ordinal);
            if (braceIndex < 0)
            {
                continue;
            }

            line = index + 1;
            column = braceIndex + 1;
            return true;
        }

        line = 0;
        column = 0;
        return false;
    }

    private static void AddCheck(
        List<ScriptDebugSmokeCheck> checks,
        string name,
        bool passed,
        string message)
    {
        checks.Add(new ScriptDebugSmokeCheck(
            name,
            passed ? ScriptDebugSmokeCheckStatus.Passed : ScriptDebugSmokeCheckStatus.Failed,
            message));
    }

    private static void AddSkipped(
        List<ScriptDebugSmokeCheck> checks,
        string name,
        string message)
    {
        checks.Add(new ScriptDebugSmokeCheck(name, ScriptDebugSmokeCheckStatus.Skipped, message));
    }

    private static ScriptDebugSmokeResult CreateResult(
        string scriptPath,
        string outputDirectory,
        string behaviorId,
        string? graphNodeId,
        string? debugSiteId,
        IReadOnlyList<ScriptDebugSmokeCheck> checks)
    {
        return new ScriptDebugSmokeResult(
            scriptPath,
            outputDirectory,
            behaviorId,
            graphNodeId,
            debugSiteId,
            checks.Count(check => check.Status == ScriptDebugSmokeCheckStatus.Passed),
            checks.Count(check => check.Status == ScriptDebugSmokeCheckStatus.Failed),
            checks.Count(check => check.Status == ScriptDebugSmokeCheckStatus.Skipped),
            checks);
    }
}

public static class ScriptDebugSmokeReporter
{
    public static void Write(ScriptDebugSmokeResult result, TextWriter writer)
    {
        writer.WriteLine("DebugSmoke:");
        writer.WriteLine($"  Script: {result.ScriptPath}");
        writer.WriteLine($"  Output: {result.OutputDirectory}");
        writer.WriteLine($"  BehaviorId: {result.BehaviorId}");
        writer.WriteLine($"  Target: {result.GraphNodeId ?? "<none>"} {result.DebugSiteId ?? string.Empty}".TrimEnd());
        writer.WriteLine(
            $"  Summary: passed={result.Passed.ToString(CultureInfo.InvariantCulture)} failed={result.Failed.ToString(CultureInfo.InvariantCulture)} skipped={result.Skipped.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteLine("  Checks:");

        foreach (var check in result.Checks)
        {
            writer.WriteLine($"    {check.Status} {check.Name}: {check.Message}");
        }
    }
}
