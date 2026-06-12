using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Security.Cryptography;
using System.Text;

namespace ScriptLab;

public static class ScriptBreakpointBindingStatus
{
    public const string Verified = "verified";
    public const string Bound = "bound";
    public const string Ambiguous = "ambiguous";
    public const string SourceOnly = "sourceOnly";
    public const string Unbound = "unbound";
}

public sealed record ScriptBreakpointBinding(
    string Status,
    string? DebugSiteId,
    string? GraphNodeId,
    string? FunctionId,
    BehaviorSourceSpan? SourceSpan,
    ScriptDebugMapSequencePoint? PdbSequencePoint,
    string Reason,
    IReadOnlyList<ScriptBreakpointCandidate> Candidates);

public sealed record ScriptBreakpointCandidate(
    string DebugSiteId,
    string GraphNodeId,
    string FunctionId,
    string Kind,
    string Label,
    BehaviorSourceSpan SourceSpan,
    string BreakabilityHint,
    bool BreakableVerified,
    ScriptDebugMapSequencePoint? PdbSequencePoint);

public sealed record ScriptSourceBreakpointRequest(
    int Line,
    int Column,
    string? Condition = null,
    string? HitCondition = null);

public sealed record ScriptBlueprintBreakpointRequest(
    string GraphNodeId,
    string? Condition = null,
    string? HitCondition = null);

public sealed record ScriptBreakpointAnchor(
    string BehaviorId,
    string FunctionId,
    BehaviorSourceSpan SourceSpan,
    string SourceTextHash,
    string Kind,
    string Label,
    int SiblingOrdinal);

public sealed record ScriptBreakpointState(
    string Key,
    string Status,
    bool HasSourceOrigin,
    bool HasBlueprintOrigin,
    string? DebugSiteId,
    string? GraphNodeId,
    string? FunctionId,
    string SourcePath,
    int Line,
    int Column,
    string? Condition,
    string? HitCondition,
    ScriptBreakpointAnchor? Anchor,
    ScriptBreakpointBinding Binding);

public static class ScriptStoppedEventStatus
{
    public const string Resolved = "resolved";
    public const string Ignored = "ignored";
    public const string Unresolved = "unresolved";
}

public static class ScriptStoppedReason
{
    public const string Breakpoint = "breakpoint";
    public const string Step = "step";
    public const string Pause = "pause";
    public const string Unknown = "unknown";
}

public sealed record ScriptStoppedEvent(
    string Status,
    string Reason,
    bool AllThreadsStopped,
    bool Synthetic,
    int? ThreadId,
    int? ProbeId,
    string? DebugSiteId,
    string? GraphNodeId,
    string? FunctionId,
    string? SourcePath,
    int? Line,
    int? Column,
    ScriptDebugMapSequencePoint? PdbSequencePoint,
    ScriptBreakpointBinding? Binding,
    string Message);

public static class ScriptPausedSnapshotStatus
{
    public const string Partial = "partial";
    public const string Unavailable = "unavailable";
}

public static class ScriptDebugScopeKind
{
    public const string Arguments = "arguments";
    public const string Locals = "locals";
    public const string This = "this";
    public const string Inspector = "inspector";
    public const string Watch = "watch";
}

public sealed record ScriptDebugVariable(
    string Name,
    string? VariableId,
    string? Type,
    string DisplayValue,
    object? RawValue,
    string? BehaviorId,
    int? EntityId,
    string? FieldId,
    string? Accessibility,
    string? Serialization,
    bool Writable,
    string? DebugSiteId = null,
    string? GraphNodeId = null,
    string? FunctionId = null,
    int? ProbeId = null,
    string? PinId = null,
    long? Sequence = null,
    int? HitCount = null)
{
    public int? VariablesReference { get; init; }
}

public sealed record ScriptFrameVariableSnapshot(
    bool Available,
    string Reason,
    IReadOnlyList<ScriptDebugVariable> Arguments,
    IReadOnlyList<ScriptDebugVariable> Locals,
    IReadOnlyList<ScriptDebugVariable> ThisVariables);

public interface IScriptFrameVariableBackend
{
    ScriptFrameVariableSnapshot ReadVariables(ScriptStoppedEvent stoppedEvent);
}

public sealed record ScriptDebugScope(
    string Name,
    string Kind,
    bool Available,
    string Reason,
    IReadOnlyList<ScriptDebugVariable> Variables);

public sealed record ScriptPausedSnapshot(
    string Status,
    bool Synthetic,
    string BehaviorId,
    int? EntityId,
    ScriptStoppedEvent StoppedEvent,
    IReadOnlyList<ScriptDebugScope> Scopes);

public sealed record ScriptTraceSiteSnapshot(
    string DebugSiteId,
    string GraphNodeId,
    string FunctionId,
    string Kind,
    string Label,
    int? ProbeId,
    BehaviorSourceSpan SourceSpan,
    int HitCount,
    long LastSequence);

public sealed record ScriptTraceSample(
    long Sequence,
    int ProbeId,
    string DebugSiteId,
    string GraphNodeId,
    string FunctionId,
    string Kind,
    string Label,
    BehaviorSourceSpan SourceSpan);

public sealed record ScriptTraceSnapshot(
    long Sequence,
    int RecentCapacity,
    int DroppedSampleCount,
    IReadOnlyList<ScriptTraceSiteSnapshot> Sites,
    IReadOnlyList<ScriptTraceSample> RecentSamples);

public sealed record ScriptProbeEventIngestResult(
    int Cursor,
    int ProcessedCount,
    IReadOnlyList<ScriptStoppedEvent> StoppedEvents,
    IReadOnlyList<ScriptDebugVariable> WatchVariables,
    ScriptTraceSnapshot TraceSnapshot);

public class DebugSessionCore
{
    private const int SupportedDebugMapSchemaVersion = 1;

    private readonly ScriptDebugMap debugMap;
    private readonly SourceText sourceText;
    private readonly SyntaxTree syntaxTree;
    private readonly int traceSampleCapacity;
    private readonly IReadOnlyList<IndexedSite> sites;
    private readonly Dictionary<string, IndexedSite> siteByDebugSiteId;
    private readonly Dictionary<string, IndexedSite> siteByGraphNodeId;
    private readonly Dictionary<int, IndexedSite> siteByProbeId;
    private readonly Dictionary<string, MutableBreakpointState> breakpoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableWatchValue> watchValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MutableTraceSite> traceSites = new(StringComparer.Ordinal);
    private readonly Queue<ScriptTraceSample> traceSamples = new();
    private long nextWatchSequence;
    private long nextTraceSequence;
    private int droppedTraceSamples;
    private int probeEventCursor;
    private long lastIngestedProbeSequence;
    private bool traceObservationEnabled;
    private bool watchObservationEnabled;

    public DebugSessionCore(ScriptDebugMap debugMap, string sourceText, int traceSampleCapacity = 256)
        : this(debugMap, sourceText, debugMap.SourceDocumentPath, traceSampleCapacity)
    {
    }

    public DebugSessionCore(
        ScriptDebugMap debugMap,
        string sourceText,
        string sourcePath,
        int traceSampleCapacity = 256)
    {
        ValidateDebugMap(debugMap, sourceText, sourcePath);

        this.debugMap = debugMap;
        this.sourceText = SourceText.From(sourceText);
        this.traceSampleCapacity = Math.Max(1, traceSampleCapacity);
        syntaxTree = CSharpSyntaxTree.ParseText(
            this.sourceText,
            path: debugMap.SourceDocumentPath);
        sites = debugMap.Functions
            .SelectMany(function => function.Sites.Select((site, index) =>
                new IndexedSite(function.FunctionId, site, index)))
            .ToArray();
        siteByDebugSiteId = sites
            .GroupBy(site => site.Site.DebugSiteId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        siteByGraphNodeId = sites
            .GroupBy(site => site.Site.GraphNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        siteByProbeId = sites
            .Where(site => site.Site.ProbeId is not null)
            .GroupBy(site => site.Site.ProbeId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
    }

    private static void ValidateDebugMap(ScriptDebugMap debugMap, string sourceText, string sourcePath)
    {
        if (debugMap.SchemaVersion != SupportedDebugMapSchemaVersion)
        {
            throw new InvalidOperationException(
                $"DebugMap schema version '{debugMap.SchemaVersion}' is not supported; expected '{SupportedDebugMapSchemaVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(debugMap.SourceDocumentPath))
        {
            throw new InvalidOperationException("DebugMap source document path is missing.");
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidOperationException("Debug session source path is missing.");
        }

        if (!PathsEqual(sourcePath, debugMap.SourceDocumentPath))
        {
            throw new InvalidOperationException(
                $"DebugMap source document path '{debugMap.SourceDocumentPath}' does not match source path '{sourcePath}'.");
        }

        if (string.IsNullOrWhiteSpace(debugMap.SourceChecksum))
        {
            throw new InvalidOperationException("DebugMap source checksum is missing.");
        }

        var actualChecksum = ComputeSha256Hex(sourceText);
        if (!string.Equals(debugMap.SourceChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DebugMap source checksum does not match current source text.");
        }
    }

    public IReadOnlyList<ScriptBreakpointState> Breakpoints => GetBreakpoints();

    public string BehaviorId => debugMap.BehaviorId;

    public string SourceDocumentPath => debugMap.SourceDocumentPath;

    public bool TraceObservationEnabled => traceObservationEnabled;

    public bool WatchObservationEnabled => watchObservationEnabled;

    public IReadOnlyList<ScriptBreakpointState> SetSourceBreakpoints(
        string filePath,
        IEnumerable<ScriptSourceBreakpointRequest> requests)
    {
        var fullPath = Path.GetFullPath(filePath);
        ClearSourceOrigin(fullPath);

        foreach (var request in requests)
        {
            var binding = ResolveSourceBreakpoint(fullPath, request.Line, request.Column);
            var breakpoint = UpsertBreakpoint(
                binding,
                fullPath,
                request.Line,
                request.Column);
            breakpoint.HasSourceOrigin = true;
            breakpoint.SourceCondition = request.Condition;
            breakpoint.SourceHitCondition = request.HitCondition;
        }

        RemoveBreakpointsWithoutOrigins();
        return GetDebuggerSourceBreakpointBatch(fullPath);
    }

    public IReadOnlyList<ScriptBreakpointState> SetBlueprintBreakpoints(IEnumerable<string> graphNodeIds)
    {
        return SetBlueprintBreakpoints(
            graphNodeIds.Select(graphNodeId => new ScriptBlueprintBreakpointRequest(graphNodeId)));
    }

    public IReadOnlyList<ScriptBreakpointState> SetBlueprintBreakpoints(
        IEnumerable<ScriptBlueprintBreakpointRequest> requests)
    {
        ClearBlueprintOrigin();

        foreach (var request in requests)
        {
            var binding = ResolveBlueprintBreakpoint(request.GraphNodeId);
            var breakpoint = UpsertBreakpoint(
                binding,
                debugMap.SourceDocumentPath,
                line: 0,
                column: 0,
                fallbackGraphNodeId: request.GraphNodeId);
            breakpoint.HasBlueprintOrigin = true;
            breakpoint.BlueprintCondition = request.Condition;
            breakpoint.BlueprintHitCondition = request.HitCondition;
        }

        RemoveBreakpointsWithoutOrigins();
        return GetBreakpoints();
    }

    public IReadOnlyList<ScriptBreakpointBackendResult> ApplySourceBreakpoints(
        IScriptBreakpointBackend backend,
        string filePath)
    {
        return backend.ReplaceSourceBreakpoints(
            Path.GetFullPath(filePath),
            GetDebuggerSourceBreakpointBatch(filePath));
    }

    public bool TryResolveStoppedEvent(DebugRuntimeProbeEvent probeEvent, out ScriptStoppedEvent stoppedEvent)
    {
        stoppedEvent = ResolveStoppedEvent(probeEvent);
        return stoppedEvent.Status == ScriptStoppedEventStatus.Resolved;
    }

    public ScriptStoppedEvent ResolveStoppedEvent(DebugRuntimeProbeEvent probeEvent)
    {
        if (!string.Equals(probeEvent.Kind, "Breakpoint", StringComparison.Ordinal))
        {
            return new ScriptStoppedEvent(
                ScriptStoppedEventStatus.Ignored,
                ScriptStoppedReason.Unknown,
                AllThreadsStopped: false,
                Synthetic: true,
                ThreadId: null,
                probeEvent.ProbeId,
                DebugSiteId: null,
                GraphNodeId: null,
                FunctionId: null,
                SourcePath: null,
                Line: null,
                Column: null,
                PdbSequencePoint: null,
                Binding: null,
                $"Probe event kind '{probeEvent.Kind}' is not a stopped event.");
        }

        return ResolveStoppedProbe(probeEvent.ProbeId, synthetic: true);
    }

    public ScriptStoppedEvent ResolveStoppedProbe(int probeId, bool synthetic, int? threadId = null)
    {
        if (!siteByProbeId.TryGetValue(probeId, out var indexedSite))
        {
            return new ScriptStoppedEvent(
                ScriptStoppedEventStatus.Unresolved,
                ScriptStoppedReason.Breakpoint,
                AllThreadsStopped: !synthetic,
                Synthetic: synthetic,
                ThreadId: synthetic ? null : threadId,
                probeId,
                DebugSiteId: null,
                GraphNodeId: null,
                FunctionId: null,
                SourcePath: null,
                Line: null,
                Column: null,
                PdbSequencePoint: null,
                Binding: null,
                $"Probe id '{probeId}' is not present in the debug map.");
        }

        var binding = BindSite(indexedSite, "Stopped location maps directly to a verified breakpoint site.");
        var location = ResolveBreakpointLocation(
            binding,
            debugMap.SourceDocumentPath,
            indexedSite.Site.SourceSpan.Line,
            indexedSite.Site.SourceSpan.Column);

        return new ScriptStoppedEvent(
            ScriptStoppedEventStatus.Resolved,
            ScriptStoppedReason.Breakpoint,
            AllThreadsStopped: !synthetic,
            Synthetic: synthetic,
            ThreadId: synthetic ? null : threadId,
            probeId,
            binding.DebugSiteId ?? indexedSite.Site.DebugSiteId,
            binding.GraphNodeId ?? indexedSite.Site.GraphNodeId,
            binding.FunctionId ?? indexedSite.FunctionId,
            location.SourcePath,
            location.Line,
            location.Column,
            binding.PdbSequencePoint,
            binding,
            synthetic
                ? "Resolved synthetic probe breakpoint event to debug map selection."
                : "Resolved debugger breakpoint stop to debug map selection.");
    }

    public ScriptStoppedEvent ResolveDebuggerStoppedFrame(
        string reason,
        bool allThreadsStopped,
        int? threadId,
        string sourcePath,
        int line,
        int column)
    {
        var binding = ResolveSourceBreakpoint(sourcePath, line, column);
        var normalizedReason = NormalizeStoppedReason(reason);
        var location = ResolveBreakpointLocation(
            binding,
            sourcePath,
            line,
            column);

        if (binding.Status == ScriptBreakpointBindingStatus.Unbound)
        {
            return new ScriptStoppedEvent(
                ScriptStoppedEventStatus.Unresolved,
                normalizedReason,
                allThreadsStopped,
                Synthetic: false,
                threadId,
                ProbeId: null,
                DebugSiteId: null,
                GraphNodeId: null,
                FunctionId: null,
                location.SourcePath,
                location.Line,
                location.Column,
                PdbSequencePoint: null,
                Binding: binding,
                "Debugger stopped frame could not be resolved to a debug map site.");
        }

        return new ScriptStoppedEvent(
            ScriptStoppedEventStatus.Resolved,
            normalizedReason,
            allThreadsStopped,
            Synthetic: false,
            threadId,
            ProbeId: null,
            binding.DebugSiteId,
            binding.GraphNodeId,
            binding.FunctionId,
            location.SourcePath,
            location.Line,
            location.Column,
            binding.PdbSequencePoint,
            binding,
            binding.Status == ScriptBreakpointBindingStatus.SourceOnly
                ? "Resolved debugger stopped frame to source-only script location."
                : "Resolved debugger stopped frame to debug map selection.");
    }

    public ScriptProbeEventIngestResult IngestProbeEvents(IReadOnlyList<DebugRuntimeProbeEvent> probeEvents)
    {
        if (probeEventCursor > 0 &&
            (probeEvents.Count < probeEventCursor ||
             probeEvents[probeEventCursor - 1].Sequence != lastIngestedProbeSequence))
        {
            probeEventCursor = 0;
        }

        var stoppedEvents = new List<ScriptStoppedEvent>();
        var processedCount = 0;

        for (var index = probeEventCursor; index < probeEvents.Count; index++)
        {
            var probeEvent = probeEvents[index];
            processedCount++;
            if (watchObservationEnabled)
            {
                RecordWatchValue(probeEvent);
            }

            if (traceObservationEnabled)
            {
                RecordTraceEvent(probeEvent);
            }

            var stoppedEvent = ResolveStoppedEvent(probeEvent);
            if (stoppedEvent.Status != ScriptStoppedEventStatus.Ignored)
            {
                stoppedEvents.Add(stoppedEvent);
            }
        }

        probeEventCursor = probeEvents.Count;
        lastIngestedProbeSequence = probeEventCursor == 0
            ? 0
            : probeEvents[probeEventCursor - 1].Sequence;
        return new ScriptProbeEventIngestResult(
            probeEventCursor,
            processedCount,
            stoppedEvents,
            GetWatchVariables(),
            GetTraceSnapshot());
    }

    public void ResetProbeEventCursor()
    {
        probeEventCursor = 0;
        lastIngestedProbeSequence = 0;
    }

    public void SetTraceObservationEnabled(bool enabled)
    {
        traceObservationEnabled = enabled;
        if (!enabled)
        {
            ClearTrace();
        }
    }

    public void SetWatchObservationEnabled(bool enabled)
    {
        watchObservationEnabled = enabled;
        if (!enabled)
        {
            ClearWatchValues();
        }
    }

    public IReadOnlyList<ScriptDebugVariable> RecordWatchValues(IEnumerable<DebugRuntimeProbeEvent> probeEvents)
    {
        foreach (var probeEvent in probeEvents)
        {
            RecordWatchValue(probeEvent);
        }

        return GetWatchVariables();
    }

    public void ClearWatchValues()
    {
        watchValues.Clear();
    }

    public ScriptTraceSnapshot RecordTraceEvents(IEnumerable<DebugRuntimeProbeEvent> probeEvents)
    {
        foreach (var probeEvent in probeEvents)
        {
            RecordTraceEvent(probeEvent);
        }

        return GetTraceSnapshot();
    }

    public ScriptTraceSnapshot GetTraceSnapshot()
    {
        return new ScriptTraceSnapshot(
            nextTraceSequence,
            traceSampleCapacity,
            droppedTraceSamples,
            traceSites.Values
                .OrderByDescending(site => site.LastSequence)
                .ThenBy(site => site.Site.Site.DebugSiteId, StringComparer.Ordinal)
                .Select(site => new ScriptTraceSiteSnapshot(
                    site.Site.Site.DebugSiteId,
                    site.Site.Site.GraphNodeId,
                    site.Site.FunctionId,
                    site.Site.Site.Kind,
                    site.Site.Site.Label,
                    site.Site.Site.ProbeId,
                    site.Site.Site.SourceSpan,
                    site.HitCount,
                    site.LastSequence))
                .ToArray(),
            traceSamples.ToArray());
    }

    public void ClearTrace()
    {
        traceSites.Clear();
        traceSamples.Clear();
        nextTraceSequence = 0;
        droppedTraceSamples = 0;
    }

    public ScriptPausedSnapshot ReadPausedSnapshot(
        DotnetDebugHost host,
        ScriptStoppedEvent stoppedEvent,
        int entityId,
        IScriptFrameVariableBackend? frameVariables = null)
    {
        if (stoppedEvent.Status != ScriptStoppedEventStatus.Resolved)
        {
            return new ScriptPausedSnapshot(
                ScriptPausedSnapshotStatus.Unavailable,
                stoppedEvent.Synthetic,
                debugMap.BehaviorId,
                EntityId: entityId,
                stoppedEvent,
                CreateUnavailableScopes("Stopped event is not resolved to a debug map site."));
        }

        var frameScopes = CreateFrameScopes(stoppedEvent, frameVariables);

        return new ScriptPausedSnapshot(
            ScriptPausedSnapshotStatus.Partial,
            stoppedEvent.Synthetic,
            debugMap.BehaviorId,
            entityId,
            stoppedEvent,
            frameScopes.Concat(new[]
            {
                CreateInspectorScope(host, entityId),
                CreateWatchScope()
            }).ToArray());
    }

    private void RecordTraceEvent(DebugRuntimeProbeEvent probeEvent)
    {
        if (!string.Equals(probeEvent.Kind, "Enter", StringComparison.Ordinal) ||
            !siteByProbeId.TryGetValue(probeEvent.ProbeId, out var indexedSite))
        {
            return;
        }

        var site = indexedSite.Site;
        var sequence = ++nextTraceSequence;
        var sample = new ScriptTraceSample(
            sequence,
            probeEvent.ProbeId,
            site.DebugSiteId,
            site.GraphNodeId,
            indexedSite.FunctionId,
            site.Kind,
            site.Label,
            site.SourceSpan);

        if (!traceSites.TryGetValue(site.DebugSiteId, out var traceSite))
        {
            traceSite = new MutableTraceSite(indexedSite);
            traceSites.Add(site.DebugSiteId, traceSite);
        }

        traceSite.HitCount++;
        traceSite.LastSequence = sequence;
        traceSamples.Enqueue(sample);

        while (traceSamples.Count > traceSampleCapacity)
        {
            traceSamples.Dequeue();
            droppedTraceSamples++;
        }
    }

    public IReadOnlyList<ScriptBreakpointState> GetDebuggerSourceBreakpointBatch(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        return GetBreakpoints()
            .Where(breakpoint =>
                PathsEqual(breakpoint.SourcePath, fullPath) &&
                breakpoint.Status is
                    ScriptBreakpointBindingStatus.Verified or
                    ScriptBreakpointBindingStatus.Bound or
                    ScriptBreakpointBindingStatus.SourceOnly)
            .ToArray();
    }

    public ScriptBreakpointBinding ResolveBlueprintBreakpoint(string graphNodeId)
    {
        if (!siteByGraphNodeId.TryGetValue(graphNodeId, out var indexedSite))
        {
            return CreateUnbound(
                $"Graph node '{graphNodeId}' is not present in the debug map.",
                Array.Empty<ScriptBreakpointCandidate>());
        }

        return BindSite(indexedSite, "Graph node maps directly to a verified breakpoint site.");
    }

    public ScriptBreakpointBinding ResolveSourceBreakpoint(string filePath, int line, int column)
    {
        if (!PathsEqual(filePath, debugMap.SourceDocumentPath))
        {
            return CreateSourceOnly(
                $"Source file '{filePath}' is not the script document for this debug session.",
                Array.Empty<ScriptBreakpointCandidate>());
        }

        if (line <= 0 || column <= 0)
        {
            return CreateSourceOnly(
                "Breakpoint line and column must be one-based positive values.",
                Array.Empty<ScriptBreakpointCandidate>());
        }

        var hasSourcePosition = TryGetPosition(line, column, out var position);
        var exactSequencePointCandidates = sites
            .Where(site =>
                site.Site.PdbSequencePoint is not null &&
                PathsEqual(site.Site.PdbSequencePoint.DocumentPath, filePath) &&
                site.Site.PdbSequencePoint.StartLine == line &&
                site.Site.PdbSequencePoint.StartColumn == column)
            .ToArray();
        if (TryBindCandidates(
                exactSequencePointCandidates,
                "Source position matches a verified PDB sequence point.",
                out var exactBinding))
        {
            return exactBinding;
        }

        if (TryResolveBracePosition(line, column, out var braceBinding))
        {
            return braceBinding;
        }

        var containingCandidates = sites
            .Where(site => SourceSpanContains(site.Site.SourceSpan, line, column, hasSourcePosition, position))
            .OrderBy(site => site.Site.SourceSpan.Length)
            .ThenBy(site => site.Site.SourceSpan.Start)
            .ToArray();
        if (TryBindCandidates(
                containingCandidates,
                "Source position is inside a debug map source span.",
                out var containingBinding))
        {
            return containingBinding;
        }

        return CreateSourceOnly(
            "Source position does not map to a blueprint debug site.",
            Array.Empty<ScriptBreakpointCandidate>());
    }

    private IReadOnlyList<ScriptBreakpointState> GetBreakpoints()
    {
        return breakpoints.Values
            .Select(breakpoint => breakpoint.ToState())
            .OrderBy(breakpoint => breakpoint.SourcePath, StringComparer.Ordinal)
            .ThenBy(breakpoint => breakpoint.Line)
            .ThenBy(breakpoint => breakpoint.Column)
            .ThenBy(breakpoint => breakpoint.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private void ClearSourceOrigin(string filePath)
    {
        foreach (var breakpoint in breakpoints.Values)
        {
            if (PathsEqual(breakpoint.SourcePath, filePath))
            {
                breakpoint.HasSourceOrigin = false;
                breakpoint.SourceCondition = null;
                breakpoint.SourceHitCondition = null;
            }
        }
    }

    private void ClearBlueprintOrigin()
    {
        foreach (var breakpoint in breakpoints.Values)
        {
            breakpoint.HasBlueprintOrigin = false;
            breakpoint.BlueprintCondition = null;
            breakpoint.BlueprintHitCondition = null;
        }
    }

    private void RemoveBreakpointsWithoutOrigins()
    {
        foreach (var key in breakpoints
                     .Where(pair => !pair.Value.HasSourceOrigin && !pair.Value.HasBlueprintOrigin)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            breakpoints.Remove(key);
        }
    }

    private MutableBreakpointState UpsertBreakpoint(
        ScriptBreakpointBinding binding,
        string fallbackSourcePath,
        int line,
        int column,
        string? fallbackGraphNodeId = null)
    {
        var location = ResolveBreakpointLocation(binding, fallbackSourcePath, line, column);
        var key = CreateBreakpointKey(binding, location.SourcePath, location.Line, location.Column, fallbackGraphNodeId);

        if (!breakpoints.TryGetValue(key, out var breakpoint))
        {
            breakpoint = new MutableBreakpointState(key);
            breakpoints.Add(key, breakpoint);
        }

        breakpoint.Binding = binding;
        breakpoint.SourcePath = location.SourcePath;
        breakpoint.Line = location.Line;
        breakpoint.Column = location.Column;
        breakpoint.Anchor = CreateAnchor(binding, fallbackGraphNodeId);
        return breakpoint;
    }

    private ScriptBreakpointAnchor? CreateAnchor(
        ScriptBreakpointBinding binding,
        string? fallbackGraphNodeId)
    {
        if (!string.IsNullOrWhiteSpace(fallbackGraphNodeId) &&
            siteByGraphNodeId.TryGetValue(fallbackGraphNodeId, out var graphSite))
        {
            return CreateAnchor(graphSite);
        }

        if (!string.IsNullOrWhiteSpace(binding.DebugSiteId) &&
            siteByDebugSiteId.TryGetValue(binding.DebugSiteId, out var debugSite))
        {
            return CreateAnchor(debugSite);
        }

        return null;
    }

    private ScriptBreakpointAnchor CreateAnchor(IndexedSite site)
    {
        return new ScriptBreakpointAnchor(
            debugMap.BehaviorId,
            site.FunctionId,
            site.Site.SourceSpan,
            site.Site.SourceTextHash,
            site.Site.Kind,
            site.Site.Label,
            site.SiblingOrdinal);
    }

    private static BreakpointLocation ResolveBreakpointLocation(
        ScriptBreakpointBinding binding,
        string fallbackSourcePath,
        int fallbackLine,
        int fallbackColumn)
    {
        if (binding.PdbSequencePoint is not null)
        {
            return new BreakpointLocation(
                NormalizeSourcePath(binding.PdbSequencePoint.DocumentPath, fallbackSourcePath),
                binding.PdbSequencePoint.StartLine,
                binding.PdbSequencePoint.StartColumn);
        }

        if (binding.SourceSpan is { Line: > 0, Column: > 0 } sourceSpan)
        {
            return new BreakpointLocation(
                Path.GetFullPath(fallbackSourcePath),
                sourceSpan.Line,
                sourceSpan.Column);
        }

        return new BreakpointLocation(Path.GetFullPath(fallbackSourcePath), fallbackLine, fallbackColumn);
    }

    private static string NormalizeSourcePath(string sourcePath, string fallbackSourcePath)
    {
        return PathsEqual(sourcePath, fallbackSourcePath)
            ? Path.GetFullPath(fallbackSourcePath)
            : Path.GetFullPath(sourcePath);
    }

    private static string CreateBreakpointKey(
        ScriptBreakpointBinding binding,
        string sourcePath,
        int line,
        int column,
        string? fallbackGraphNodeId)
    {
        if (!string.IsNullOrWhiteSpace(binding.DebugSiteId))
        {
            return $"debug:{binding.DebugSiteId}";
        }

        if (!string.IsNullOrWhiteSpace(binding.GraphNodeId))
        {
            return $"graph:{binding.GraphNodeId}";
        }

        if (!string.IsNullOrWhiteSpace(fallbackGraphNodeId))
        {
            return $"graph:{fallbackGraphNodeId}";
        }

        return $"source:{Path.GetFullPath(sourcePath)}:{line}:{column}";
    }

    private bool TryResolveBracePosition(int line, int column, out ScriptBreakpointBinding binding)
    {
        binding = null!;

        if (!TryGetPosition(line, column, out var position))
        {
            return false;
        }

        var root = syntaxTree.GetRoot();
        var token = root.FindToken(position);
        if (!token.IsKind(SyntaxKind.OpenBraceToken))
        {
            return false;
        }

        if (token.Parent is not BlockSyntax block)
        {
            return false;
        }

        if (block.Parent is IfStatementSyntax ifStatement)
        {
            var ifSpan = ifStatement.GetLocation().GetLineSpan().Span;
            var branchCandidates = sites
                .Where(site =>
                    site.Site.Kind == "Branch" &&
                    site.Site.SourceSpan.Line == ifSpan.Start.Line + 1 &&
                    site.Site.SourceSpan.Column == ifSpan.Start.Character + 1)
                .ToArray();
            if (TryBindCandidates(
                    branchCandidates,
                    "Opening brace belongs to an if block; binding to the owning Branch statement.",
                    out binding))
            {
                return true;
            }
        }

        var blockSpan = block.GetLocation().GetLineSpan().Span;
        var firstVerifiedSiteInBlock = sites
            .Where(site =>
                site.Site.BreakableVerified &&
                site.Site.SourceSpan.Line >= blockSpan.Start.Line + 1 &&
                site.Site.SourceSpan.Line <= blockSpan.End.Line + 1)
            .OrderBy(site => site.Site.SourceSpan.Line)
            .ThenBy(site => site.Site.SourceSpan.Column)
            .Take(1)
            .ToArray();
        return TryBindCandidates(
            firstVerifiedSiteInBlock,
            "Opening brace belongs to a block; binding to the first verified breakpoint site in that block.",
            out binding);
    }

    private bool TryBindCandidates(
        IReadOnlyList<IndexedSite> candidateSites,
        string verifiedReason,
        out ScriptBreakpointBinding binding)
    {
        binding = null!;

        if (candidateSites.Count == 0)
        {
            return false;
        }

        var verifiedCandidates = candidateSites
            .Where(site => site.Site.BreakableVerified)
            .ToArray();
        if (verifiedCandidates.Length == 1)
        {
            binding = CreateBinding(
                ScriptBreakpointBindingStatus.Verified,
                verifiedCandidates[0],
                verifiedReason,
                candidateSites);
            return true;
        }

        if (verifiedCandidates.Length > 1)
        {
            binding = CreateAmbiguous(
                "Source position maps to multiple verified breakpoint sites.",
                verifiedCandidates);
            return true;
        }

        var ownerCandidates = candidateSites
            .Select(site => site.Site.OwningBreakableDebugSiteId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Select(id => siteByDebugSiteId.TryGetValue(id!, out var owner) ? owner : null)
            .Where(site => site is not null && site.Site.BreakableVerified)
            .Cast<IndexedSite>()
            .ToArray();
        if (ownerCandidates.Length == 1)
        {
            binding = CreateBinding(
                ScriptBreakpointBindingStatus.Bound,
                ownerCandidates[0],
                "Source position maps to an observable expression; binding to its owning verified statement.",
                candidateSites);
            return true;
        }

        if (ownerCandidates.Length > 1)
        {
            binding = CreateAmbiguous(
                "Source position maps to observable expressions with multiple owning breakpoint sites.",
                ownerCandidates);
            return true;
        }

        binding = CreateSourceOnly(
            "Source position is represented in the debug map but has no verified breakpoint location.",
            candidateSites.Select(ToCandidate).ToArray());
        return true;
    }

    private ScriptBreakpointBinding BindSite(IndexedSite indexedSite, string verifiedReason)
    {
        if (indexedSite.Site.BreakableVerified)
        {
            return CreateBinding(
                ScriptBreakpointBindingStatus.Verified,
                indexedSite,
                verifiedReason,
                new[] { indexedSite });
        }

        if (!string.IsNullOrWhiteSpace(indexedSite.Site.OwningBreakableDebugSiteId) &&
            siteByDebugSiteId.TryGetValue(indexedSite.Site.OwningBreakableDebugSiteId, out var owner) &&
            owner.Site.BreakableVerified)
        {
            return CreateBinding(
                ScriptBreakpointBindingStatus.Bound,
                owner,
                "Graph node is not independently breakable; binding to its owning verified statement.",
                new[] { indexedSite, owner });
        }

        return CreateUnbound(
            "Graph node has no verified breakpoint location and no owning verified statement.",
            new[] { ToCandidate(indexedSite) });
    }

    private ScriptDebugScope CreateInspectorScope(DotnetDebugHost host, int entityId)
    {
        var fields = host.GetFields(entityId, debugMap.BehaviorId);
        return new ScriptDebugScope(
            "Inspector",
            ScriptDebugScopeKind.Inspector,
            Available: true,
            $"Read from mounted behavior instance '{debugMap.BehaviorId}' on entity {entityId}.",
            fields.Select(field => new ScriptDebugVariable(
                    field.Name,
                    $"field:{field.BehaviorId}:{field.EntityId}:{field.FieldId}",
                    field.Type,
                    FormatVariableValue(field.Value),
                    field.Value,
                    field.BehaviorId,
                    field.EntityId,
                    field.FieldId.ToString(),
                    field.Accessibility,
                    field.Serialization,
                    Writable: true))
                .ToArray());
    }

    private void RecordWatchValue(DebugRuntimeProbeEvent probeEvent)
    {
        if (!string.Equals(probeEvent.Kind, "Value", StringComparison.Ordinal) ||
            !siteByProbeId.TryGetValue(probeEvent.ProbeId, out var indexedSite) ||
            !string.Equals(indexedSite.Site.Kind, "Watch", StringComparison.Ordinal))
        {
            return;
        }

        var pinId = string.IsNullOrWhiteSpace(probeEvent.PinId)
            ? indexedSite.Site.Label
            : probeEvent.PinId!;
        var key = $"watch:{indexedSite.Site.DebugSiteId}:{pinId}";

        if (!watchValues.TryGetValue(key, out var watchValue))
        {
            watchValue = new MutableWatchValue(key, indexedSite, pinId);
            watchValues.Add(key, watchValue);
        }

        watchValue.RawValue = probeEvent.Value;
        watchValue.Sequence = ++nextWatchSequence;
        watchValue.HitCount++;
    }

    private ScriptDebugScope CreateWatchScope()
    {
        var variables = GetWatchVariables();
        if (variables.Count == 0)
        {
            return CreateUnavailableScope(
                "Watch / Pin Inspect",
                ScriptDebugScopeKind.Watch,
                "No Watch or Pin Inspect values have been recorded.");
        }

        return new ScriptDebugScope(
            "Watch / Pin Inspect",
            ScriptDebugScopeKind.Watch,
            Available: true,
            "Last observed values from explicit DebugProbe.Value events.",
            variables);
    }

    private IReadOnlyList<ScriptDebugVariable> GetWatchVariables()
    {
        return watchValues.Values
            .OrderBy(value => value.Sequence)
            .Select(value =>
            {
                var site = value.Site.Site;
                return new ScriptDebugVariable(
                    value.PinId,
                    value.Key,
                    GetFriendlyValueTypeName(value.RawValue),
                    FormatVariableValue(value.RawValue),
                    value.RawValue,
                    debugMap.BehaviorId,
                    EntityId: null,
                    FieldId: null,
                    Accessibility: null,
                    Serialization: null,
                    Writable: false,
                    site.DebugSiteId,
                    site.GraphNodeId,
                    value.Site.FunctionId,
                    site.ProbeId,
                    value.PinId,
                    value.Sequence,
                    value.HitCount);
            })
            .ToArray();
    }

    private static IReadOnlyList<ScriptDebugScope> CreateUnavailableScopes(string reason)
    {
        return new[]
        {
            CreateUnavailableScope("Arguments", ScriptDebugScopeKind.Arguments, reason),
            CreateUnavailableScope("Locals", ScriptDebugScopeKind.Locals, reason),
            CreateUnavailableScope("This", ScriptDebugScopeKind.This, reason),
            CreateUnavailableScope("Inspector", ScriptDebugScopeKind.Inspector, reason),
            CreateUnavailableScope("Watch / Pin Inspect", ScriptDebugScopeKind.Watch, reason)
        };
    }

    private static IReadOnlyList<ScriptDebugScope> CreateFrameScopes(
        ScriptStoppedEvent stoppedEvent,
        IScriptFrameVariableBackend? frameVariables)
    {
        if (stoppedEvent.Synthetic)
        {
            const string reason = "Synthetic probe stops do not expose a debugger stack frame.";
            return CreateUnavailableFrameScopes(reason);
        }

        if (frameVariables is null)
        {
            const string reason = "Debugger frame variable backend is not connected yet.";
            return CreateUnavailableFrameScopes(reason);
        }

        var frameSnapshot = frameVariables.ReadVariables(stoppedEvent);
        if (!frameSnapshot.Available)
        {
            var reason = string.IsNullOrWhiteSpace(frameSnapshot.Reason)
                ? "Debugger frame variables are unavailable."
                : frameSnapshot.Reason;
            return CreateUnavailableFrameScopes(reason);
        }

        return new[]
        {
            CreateFrameScope(
                "Arguments",
                ScriptDebugScopeKind.Arguments,
                "Debugger frame arguments are available.",
                frameSnapshot.Arguments),
            CreateFrameScope(
                "Locals",
                ScriptDebugScopeKind.Locals,
                "Debugger frame locals are available.",
                frameSnapshot.Locals),
            CreateFrameScope(
                "This",
                ScriptDebugScopeKind.This,
                "Debugger frame this variables are available.",
                frameSnapshot.ThisVariables)
        };
    }

    private static IReadOnlyList<ScriptDebugScope> CreateUnavailableFrameScopes(string reason)
    {
        return new[]
        {
            CreateUnavailableScope("Arguments", ScriptDebugScopeKind.Arguments, reason),
            CreateUnavailableScope("Locals", ScriptDebugScopeKind.Locals, reason),
            CreateUnavailableScope("This", ScriptDebugScopeKind.This, reason)
        };
    }

    private static ScriptDebugScope CreateFrameScope(
        string name,
        string kind,
        string reason,
        IReadOnlyList<ScriptDebugVariable> variables)
    {
        return new ScriptDebugScope(
            name,
            kind,
            Available: true,
            reason,
            variables);
    }

    private static string? GetFriendlyValueTypeName(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        return type == typeof(bool) ? "bool" :
            type == typeof(byte) ? "byte" :
            type == typeof(short) ? "short" :
            type == typeof(int) ? "int" :
            type == typeof(long) ? "long" :
            type == typeof(float) ? "float" :
            type == typeof(double) ? "double" :
            type == typeof(decimal) ? "decimal" :
            type == typeof(string) ? "string" :
            type.FullName ?? type.Name;
    }

    private static ScriptDebugScope CreateUnavailableScope(string name, string kind, string reason)
    {
        return new ScriptDebugScope(
            name,
            kind,
            Available: false,
            reason,
            Array.Empty<ScriptDebugVariable>());
    }

    private static string FormatVariableValue(object? value)
    {
        return value switch
        {
            null => "null",
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static ScriptBreakpointBinding CreateBinding(
        string status,
        IndexedSite site,
        string reason,
        IReadOnlyList<IndexedSite> candidates)
    {
        return new ScriptBreakpointBinding(
            status,
            site.Site.DebugSiteId,
            site.Site.GraphNodeId,
            site.FunctionId,
            site.Site.SourceSpan,
            site.Site.PdbSequencePoint,
            reason,
            candidates.Select(ToCandidate).ToArray());
    }

    private static ScriptBreakpointBinding CreateAmbiguous(
        string reason,
        IReadOnlyList<IndexedSite> candidates)
    {
        return new ScriptBreakpointBinding(
            ScriptBreakpointBindingStatus.Ambiguous,
            DebugSiteId: null,
            GraphNodeId: null,
            FunctionId: null,
            SourceSpan: null,
            PdbSequencePoint: null,
            reason,
            candidates.Select(ToCandidate).ToArray());
    }

    private static ScriptBreakpointBinding CreateSourceOnly(
        string reason,
        IReadOnlyList<ScriptBreakpointCandidate> candidates)
    {
        return new ScriptBreakpointBinding(
            ScriptBreakpointBindingStatus.SourceOnly,
            DebugSiteId: null,
            GraphNodeId: null,
            FunctionId: null,
            SourceSpan: null,
            PdbSequencePoint: null,
            reason,
            candidates);
    }

    private static ScriptBreakpointBinding CreateUnbound(
        string reason,
        IReadOnlyList<ScriptBreakpointCandidate> candidates)
    {
        return new ScriptBreakpointBinding(
            ScriptBreakpointBindingStatus.Unbound,
            DebugSiteId: null,
            GraphNodeId: null,
            FunctionId: null,
            SourceSpan: null,
            PdbSequencePoint: null,
            reason,
            candidates);
    }

    private static ScriptBreakpointCandidate ToCandidate(IndexedSite site)
    {
        return new ScriptBreakpointCandidate(
            site.Site.DebugSiteId,
            site.Site.GraphNodeId,
            site.FunctionId,
            site.Site.Kind,
            site.Site.Label,
            site.Site.SourceSpan,
            site.Site.BreakabilityHint,
            site.Site.BreakableVerified,
            site.Site.PdbSequencePoint);
    }

    private static string NormalizeStoppedReason(string reason)
    {
        return reason switch
        {
            ScriptStoppedReason.Breakpoint => ScriptStoppedReason.Breakpoint,
            ScriptStoppedReason.Step => ScriptStoppedReason.Step,
            ScriptStoppedReason.Pause => ScriptStoppedReason.Pause,
            _ => ScriptStoppedReason.Unknown
        };
    }

    private bool TryGetPosition(int line, int column, out int position)
    {
        position = 0;
        var lineIndex = line - 1;
        if (lineIndex < 0 || lineIndex >= sourceText.Lines.Count)
        {
            return false;
        }

        var textLine = sourceText.Lines[lineIndex];
        var characterIndex = column - 1;
        if (characterIndex < 0 || characterIndex > textLine.Span.Length)
        {
            return false;
        }

        position = textLine.Start + characterIndex;
        return true;
    }

    private static bool SourceSpanContains(
        BehaviorSourceSpan sourceSpan,
        int line,
        int column,
        bool hasSourcePosition,
        int position)
    {
        if (sourceSpan.Line <= 0 || sourceSpan.Column <= 0)
        {
            return false;
        }

        if (hasSourcePosition && sourceSpan.Start >= 0 && sourceSpan.Length > 0)
        {
            return sourceSpan.Start <= position &&
                position < sourceSpan.Start + sourceSpan.Length;
        }

        return line == sourceSpan.Line && column == sourceSpan.Column;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static string ComputeSha256Hex(string text)
    {
        return ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string ToHex(IEnumerable<byte> bytes)
    {
        var builder = new StringBuilder();
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

    private sealed record IndexedSite(string FunctionId, ScriptDebugMapSite Site, int SiblingOrdinal);

    private sealed record BreakpointLocation(string SourcePath, int Line, int Column);

    private sealed class MutableTraceSite
    {
        public MutableTraceSite(IndexedSite site)
        {
            Site = site;
        }

        public IndexedSite Site { get; }

        public int HitCount { get; set; }

        public long LastSequence { get; set; }
    }

    private sealed class MutableWatchValue
    {
        public MutableWatchValue(string key, IndexedSite site, string pinId)
        {
            Key = key;
            Site = site;
            PinId = pinId;
        }

        public string Key { get; }

        public IndexedSite Site { get; }

        public string PinId { get; }

        public object? RawValue { get; set; }

        public long Sequence { get; set; }

        public int HitCount { get; set; }
    }

    private sealed class MutableBreakpointState
    {
        public MutableBreakpointState(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public ScriptBreakpointBinding Binding { get; set; } = null!;

        public string SourcePath { get; set; } = string.Empty;

        public int Line { get; set; }

        public int Column { get; set; }

        public bool HasSourceOrigin { get; set; }

        public bool HasBlueprintOrigin { get; set; }

        public string? SourceCondition { get; set; }

        public string? SourceHitCondition { get; set; }

        public string? BlueprintCondition { get; set; }

        public string? BlueprintHitCondition { get; set; }

        public ScriptBreakpointAnchor? Anchor { get; set; }

        public ScriptBreakpointState ToState()
        {
            return new ScriptBreakpointState(
                Key,
                Binding.Status,
                HasSourceOrigin,
                HasBlueprintOrigin,
                Binding.DebugSiteId,
                Binding.GraphNodeId,
                Binding.FunctionId,
                SourcePath,
                Line,
                Column,
                SourceCondition ?? BlueprintCondition,
                SourceHitCondition ?? BlueprintHitCondition,
                Anchor,
                Binding);
        }
    }
}

public sealed class ScriptDebugSession : DebugSessionCore
{
    public ScriptDebugSession(ScriptDebugMap debugMap, string sourceText, int traceSampleCapacity = 256)
        : base(debugMap, sourceText, traceSampleCapacity)
    {
    }

    public ScriptDebugSession(
        ScriptDebugMap debugMap,
        string sourceText,
        string sourcePath,
        int traceSampleCapacity = 256)
        : base(debugMap, sourceText, sourcePath, traceSampleCapacity)
    {
    }
}
