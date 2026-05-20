using System.Globalization;

namespace ScriptLab;

public sealed record ScriptDebugLoopOptions(
    string? GraphNodeId = null,
    int EntityId = 1,
    float Delta = 0.016f,
    bool PressKeyW = true,
    bool ObserveTrace = true,
    bool ObserveWatch = true);

public sealed record ScriptDebugLoopResult(
    string AssemblyPath,
    string PdbPath,
    string DebugMapPath,
    string BehaviorId,
    int EntityId,
    string? TargetGraphNodeId,
    string? TargetDebugSiteId,
    IReadOnlyList<ScriptBreakpointState> Breakpoints,
    IReadOnlyList<ScriptBreakpointBackendResult> BackendResults,
    IReadOnlyList<DebugRuntimeProbeEvent> ProbeEvents,
    ScriptProbeEventIngestResult Ingest,
    ScriptStoppedEvent? StoppedEvent,
    ScriptPausedSnapshot? PausedSnapshot);

public static class ScriptDebugLoop
{
    public static ScriptDebugLoopResult RunFile(
        string scriptPath,
        string outputDirectory,
        ScriptDebugLoopOptions? options = null)
    {
        options ??= new ScriptDebugLoopOptions();

        var emit = DebugScriptCompiler.EmitFile(scriptPath, outputDirectory);
        var sourceText = File.ReadAllText(emit.DebugMap.SourceDocumentPath);
        var session = new ScriptDebugSession(emit.DebugMap, sourceText, emit.DebugMap.SourceDocumentPath);
        var host = DebugScriptHost.Load(emit);
        var backend = new ProbeScriptBreakpointBackend(host);
        session.SetTraceObservationEnabled(options.ObserveTrace);
        session.SetWatchObservationEnabled(options.ObserveWatch);
        host.SetTraceEnabled(options.ObserveTrace);
        host.SetWatchEnabled(options.ObserveWatch);
        var graphNodeId = options.GraphNodeId ?? FindDefaultGraphNodeId(emit.DebugMap);

        IReadOnlyList<ScriptBreakpointState> breakpoints = Array.Empty<ScriptBreakpointState>();
        IReadOnlyList<ScriptBreakpointBackendResult> backendResults = Array.Empty<ScriptBreakpointBackendResult>();
        if (!string.IsNullOrWhiteSpace(graphNodeId))
        {
            breakpoints = session.SetBlueprintBreakpoints(new[] { graphNodeId });
            backendResults = session.ApplySourceBreakpoints(backend, emit.DebugMap.SourceDocumentPath);
        }

        var instance = host.MountBehavior(options.EntityId, emit.DebugMap.BehaviorId);
        host.ClearInput();
        if (options.PressKeyW)
        {
            host.SetInputKeyDown("W", isDown: true);
        }

        host.ClearProbeEvents();
        instance.InvokeUpdate(options.Delta);

        var probeEvents = host.GetProbeEvents();
        var ingest = session.IngestProbeEvents(probeEvents);
        var stoppedEvent = ingest.StoppedEvents.LastOrDefault();
        var pausedSnapshot = stoppedEvent is null
            ? null
            : session.ReadPausedSnapshot(host, stoppedEvent, options.EntityId);
        var targetBreakpoint = breakpoints.FirstOrDefault(breakpoint => breakpoint.HasBlueprintOrigin);

        return new ScriptDebugLoopResult(
            emit.AssemblyPath,
            emit.PdbPath,
            emit.DebugMapPath,
            emit.DebugMap.BehaviorId,
            options.EntityId,
            graphNodeId,
            targetBreakpoint?.DebugSiteId,
            breakpoints,
            backendResults,
            probeEvents,
            ingest,
            stoppedEvent,
            pausedSnapshot);
    }

    private static string? FindDefaultGraphNodeId(ScriptDebugMap debugMap)
    {
        var sites = debugMap.Functions.SelectMany(function => function.Sites).ToArray();
        return sites.FirstOrDefault(site =>
                site.BreakableVerified &&
                site.ProbeId is not null)
            ?.GraphNodeId
            ?? sites.FirstOrDefault(site => site.ProbeId is not null)?.GraphNodeId;
    }
}

public static class ScriptDebugLoopReporter
{
    public static void Write(ScriptDebugLoopResult result, TextWriter writer)
    {
        writer.WriteLine("DebugLoop:");
        writer.WriteLine($"  BehaviorId: {result.BehaviorId}");
        writer.WriteLine($"  Entity: {result.EntityId.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteLine($"  DebugMap: {result.DebugMapPath}");
        writer.WriteLine($"  Target: {result.TargetGraphNodeId ?? "<none>"} {result.TargetDebugSiteId ?? string.Empty}".TrimEnd());

        writer.WriteLine("  Breakpoints:");
        WriteBreakpoints(result.Breakpoints, writer);

        writer.WriteLine("  Backend:");
        WriteBackendResults(result.BackendResults, writer);

        writer.WriteLine("  ProbeEvents:");
        WriteProbeEvents(result.ProbeEvents, writer);

        writer.WriteLine("  Stopped:");
        WriteStoppedEvent(result.StoppedEvent, writer);

        writer.WriteLine("  Trace:");
        WriteTrace(result.Ingest.TraceSnapshot, writer);

        writer.WriteLine("  Watch:");
        WriteWatch(result.Ingest.WatchVariables, writer);

        writer.WriteLine("  Inspector:");
        WriteInspector(result.PausedSnapshot, writer);
    }

    private static void WriteBreakpoints(IReadOnlyList<ScriptBreakpointState> breakpoints, TextWriter writer)
    {
        if (breakpoints.Count == 0)
        {
            writer.WriteLine("    <none>");
            return;
        }

        foreach (var breakpoint in breakpoints)
        {
            writer.WriteLine(
                $"    {breakpoint.Status} {breakpoint.DebugSiteId ?? breakpoint.Key} graph={breakpoint.GraphNodeId ?? "<none>"} source={breakpoint.Line}:{breakpoint.Column}");
        }
    }

    private static void WriteBackendResults(IReadOnlyList<ScriptBreakpointBackendResult> results, TextWriter writer)
    {
        if (results.Count == 0)
        {
            writer.WriteLine("    <none>");
            return;
        }

        foreach (var result in results)
        {
            writer.WriteLine(
                $"    {result.Status} verified={result.Verified.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} synthetic={result.Synthetic.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} probe={result.ProbeId?.ToString(CultureInfo.InvariantCulture) ?? "<none>"} {result.Message}");
        }
    }

    private static void WriteProbeEvents(IReadOnlyList<DebugRuntimeProbeEvent> events, TextWriter writer)
    {
        if (events.Count == 0)
        {
            writer.WriteLine("    <none>");
            return;
        }

        foreach (var probeEvent in events)
        {
            var pin = probeEvent.PinId is null ? string.Empty : $" pin={probeEvent.PinId}";
            var value = probeEvent.Value is null ? string.Empty : $" value={FormatValue(probeEvent.Value)}";
            writer.WriteLine(
                $"    #{probeEvent.Sequence.ToString(CultureInfo.InvariantCulture)} {probeEvent.Kind} probe={probeEvent.ProbeId.ToString(CultureInfo.InvariantCulture)}{pin}{value}");
        }
    }

    private static void WriteStoppedEvent(ScriptStoppedEvent? stoppedEvent, TextWriter writer)
    {
        if (stoppedEvent is null)
        {
            writer.WriteLine("    <none>");
            return;
        }

        writer.WriteLine(
            $"    {stoppedEvent.Status} reason={stoppedEvent.Reason} synthetic={stoppedEvent.Synthetic.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()} graph={stoppedEvent.GraphNodeId ?? "<none>"} source={stoppedEvent.Line?.ToString(CultureInfo.InvariantCulture) ?? "?"}:{stoppedEvent.Column?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
    }

    private static void WriteTrace(ScriptTraceSnapshot trace, TextWriter writer)
    {
        if (trace.Sites.Count == 0)
        {
            writer.WriteLine("    <none>");
            return;
        }

        foreach (var site in trace.Sites)
        {
            writer.WriteLine(
                $"    {site.DebugSiteId} graph={site.GraphNodeId} kind={site.Kind} hits={site.HitCount.ToString(CultureInfo.InvariantCulture)} last={site.LastSequence.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static void WriteWatch(IReadOnlyList<ScriptDebugVariable> variables, TextWriter writer)
    {
        if (variables.Count == 0)
        {
            writer.WriteLine("    <none>");
            return;
        }

        foreach (var variable in variables)
        {
            writer.WriteLine(
                $"    {variable.Name}={variable.DisplayValue} site={variable.DebugSiteId ?? "<none>"} hits={variable.HitCount?.ToString(CultureInfo.InvariantCulture) ?? "0"}");
        }
    }

    private static void WriteInspector(ScriptPausedSnapshot? snapshot, TextWriter writer)
    {
        var inspector = snapshot?.Scopes.FirstOrDefault(scope => scope.Kind == ScriptDebugScopeKind.Inspector);
        if (inspector is not { Available: true } || inspector.Variables.Count == 0)
        {
            writer.WriteLine("    <none>");
            return;
        }

        foreach (var variable in inspector.Variables)
        {
            writer.WriteLine(
                $"    {variable.Name}:{variable.Type ?? "value"}={variable.DisplayValue} field={variable.FieldId ?? "<none>"}");
        }
    }

    private static string FormatValue(object value)
    {
        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;
    }
}
