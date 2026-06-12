using ScriptLab;

namespace ScriptLab.Tests;

public sealed class ScriptDebugSessionTests
{
    [Fact]
    public void CompatibilityWrapper_WhenConstructed_ExposesDebugSessionCore()
    {
        var emit = EmitPlayerMove();
        var sourceText = File.ReadAllText(emit.DebugMap.SourceDocumentPath);

        DebugSessionCore session = new ScriptDebugSession(emit.DebugMap, sourceText, emit.DebugMap.SourceDocumentPath);

        Assert.Equal(emit.DebugMap.BehaviorId, session.BehaviorId);
    }

    [Fact]
    public void Constructor_WhenDebugMapSchemaVersionDoesNotMatch_Throws()
    {
        var emit = EmitPlayerMove();
        var sourceText = File.ReadAllText(emit.DebugMap.SourceDocumentPath);
        var invalidMap = emit.DebugMap with { SchemaVersion = 2 };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ScriptDebugSession(invalidMap, sourceText, emit.DebugMap.SourceDocumentPath));

        Assert.Contains("schema version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WhenSourceChecksumDoesNotMatch_Throws()
    {
        var emit = EmitPlayerMove();
        var sourceText = File.ReadAllText(emit.DebugMap.SourceDocumentPath) + Environment.NewLine;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ScriptDebugSession(emit.DebugMap, sourceText, emit.DebugMap.SourceDocumentPath));

        Assert.Contains("source checksum", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WhenSourcePathDoesNotMatch_Throws()
    {
        var emit = EmitPlayerMove();
        var sourceText = File.ReadAllText(emit.DebugMap.SourceDocumentPath);
        var otherPath = Path.Combine(Path.GetDirectoryName(emit.DebugMap.SourceDocumentPath)!, "Other.ash.cs");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ScriptDebugSession(emit.DebugMap, sourceText, otherPath));

        Assert.Contains("source document path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveBlueprintBreakpoint_WhenNodeIsVerified_ReturnsVerifiedDebugSite()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var session = CreateSession(emit);

        var binding = session.ResolveBlueprintBreakpoint(branchSite.GraphNodeId);

        Assert.Equal(ScriptBreakpointBindingStatus.Verified, binding.Status);
        Assert.Equal(branchSite.DebugSiteId, binding.DebugSiteId);
        Assert.Equal(branchSite.GraphNodeId, binding.GraphNodeId);
        Assert.Equal("Update", binding.FunctionId);
        Assert.NotNull(binding.PdbSequencePoint);
    }

    [Fact]
    public void ResolveBlueprintBreakpoint_WhenNodeIsObservable_ReturnsOwningVerifiedSite()
    {
        var emit = EmitPlayerMove();
        var multiplySite = FindSite(emit.DebugMap, "BinaryOp", "Multiply");
        var translateSite = FindSite(emit.DebugMap, "Call", "asharia.transform.translate");
        var session = CreateSession(emit);

        var binding = session.ResolveBlueprintBreakpoint(multiplySite.GraphNodeId);

        Assert.Equal(ScriptBreakpointBindingStatus.Bound, binding.Status);
        Assert.Equal(translateSite.DebugSiteId, binding.DebugSiteId);
        Assert.Equal(translateSite.GraphNodeId, binding.GraphNodeId);
        Assert.Contains(binding.Candidates, candidate => candidate.DebugSiteId == multiplySite.DebugSiteId);
        Assert.Contains(binding.Candidates, candidate => candidate.DebugSiteId == translateSite.DebugSiteId);
    }

    [Fact]
    public void SetBlueprintBreakpoints_WhenNodeBindsToOwningStatement_KeepsAnchorForRequestedGraphNode()
    {
        var emit = EmitPlayerMove();
        var multiplySite = FindSite(emit.DebugMap, "BinaryOp", "Multiply");
        var translateSite = FindSite(emit.DebugMap, "Call", "asharia.transform.translate");
        var session = CreateSession(emit);

        var breakpoint = Assert.Single(session.SetBlueprintBreakpoints(new[] { multiplySite.GraphNodeId }));

        Assert.Equal(ScriptBreakpointBindingStatus.Bound, breakpoint.Status);
        Assert.Equal(translateSite.DebugSiteId, breakpoint.DebugSiteId);
        Assert.NotNull(breakpoint.Anchor);
        Assert.Equal("com.game.PlayerMove", breakpoint.Anchor.BehaviorId);
        Assert.Equal("Update", breakpoint.Anchor.FunctionId);
        Assert.Equal("BinaryOp", breakpoint.Anchor.Kind);
        Assert.Equal("Multiply", breakpoint.Anchor.Label);
        Assert.Equal(multiplySite.SourceSpan, breakpoint.Anchor.SourceSpan);
        Assert.Equal(multiplySite.SourceTextHash, breakpoint.Anchor.SourceTextHash);
        Assert.True(breakpoint.Anchor.SiblingOrdinal >= 0);
    }

    [Fact]
    public void SetSourceBreakpoints_WhenBreakpointIsVerified_AddsAnchorForResolvedSite()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var session = CreateSession(emit);

        var breakpoint = Assert.Single(session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(12, 9) }));

        Assert.Equal(ScriptBreakpointBindingStatus.Verified, breakpoint.Status);
        Assert.NotNull(breakpoint.Anchor);
        Assert.Equal("com.game.PlayerMove", breakpoint.Anchor.BehaviorId);
        Assert.Equal("Update", breakpoint.Anchor.FunctionId);
        Assert.Equal("Branch", breakpoint.Anchor.Kind);
        Assert.Equal("Branch", breakpoint.Anchor.Label);
        Assert.Equal(branchSite.SourceSpan, breakpoint.Anchor.SourceSpan);
        Assert.Equal(branchSite.SourceTextHash, breakpoint.Anchor.SourceTextHash);
    }

    [Fact]
    public void ResolveSourceBreakpoint_WhenPositionMatchesSequencePoint_ReturnsVerifiedSite()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var session = CreateSession(emit);

        var binding = session.ResolveSourceBreakpoint(emit.DebugMap.SourceDocumentPath, 12, 9);

        Assert.Equal(ScriptBreakpointBindingStatus.Verified, binding.Status);
        Assert.Equal(branchSite.DebugSiteId, binding.DebugSiteId);
        Assert.Equal(12, binding.PdbSequencePoint?.StartLine);
        Assert.Equal(9, binding.PdbSequencePoint?.StartColumn);
    }

    [Fact]
    public void ResolveSourceBreakpoint_WhenPositionIsIfOpenBrace_ReturnsBranchSite()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var session = CreateSession(emit);

        var binding = session.ResolveSourceBreakpoint(emit.DebugMap.SourceDocumentPath, 13, 9);

        Assert.Equal(ScriptBreakpointBindingStatus.Verified, binding.Status);
        Assert.Equal(branchSite.DebugSiteId, binding.DebugSiteId);
        Assert.Contains("Opening brace", binding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSourceBreakpoint_WhenPositionMatchesCallSequencePoint_ReturnsCallSite()
    {
        var emit = EmitPlayerMove();
        var translateSite = FindSite(emit.DebugMap, "Call", "asharia.transform.translate");
        var session = CreateSession(emit);

        var binding = session.ResolveSourceBreakpoint(emit.DebugMap.SourceDocumentPath, 14, 13);

        Assert.Equal(ScriptBreakpointBindingStatus.Verified, binding.Status);
        Assert.Equal(translateSite.DebugSiteId, binding.DebugSiteId);
        Assert.Equal(14, binding.PdbSequencePoint?.StartLine);
        Assert.Equal(13, binding.PdbSequencePoint?.StartColumn);
    }

    [Fact]
    public void ResolveSourceBreakpoint_WhenPositionIsNotBlueprintMapped_ReturnsSourceOnly()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);

        var binding = session.ResolveSourceBreakpoint(emit.DebugMap.SourceDocumentPath, 1, 1);

        Assert.Equal(ScriptBreakpointBindingStatus.SourceOnly, binding.Status);
        Assert.Null(binding.DebugSiteId);
        Assert.Empty(binding.Candidates);
    }

    [Fact]
    public void SetSourceBreakpoints_WhenBlueprintBreakpointExists_ReturnsWholeSourceBatchWithBlueprintBreakpoint()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var translateSite = FindSite(emit.DebugMap, "Call", "asharia.transform.translate");
        var session = CreateSession(emit);

        session.SetBlueprintBreakpoints(new[] { branchSite.GraphNodeId });
        var batch = session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(14, 13) });

        Assert.Equal(2, batch.Count);
        var branchBreakpoint = Assert.Single(batch, breakpoint => breakpoint.DebugSiteId == branchSite.DebugSiteId);
        Assert.False(branchBreakpoint.HasSourceOrigin);
        Assert.True(branchBreakpoint.HasBlueprintOrigin);
        var translateBreakpoint = Assert.Single(batch, breakpoint => breakpoint.DebugSiteId == translateSite.DebugSiteId);
        Assert.True(translateBreakpoint.HasSourceOrigin);
        Assert.False(translateBreakpoint.HasBlueprintOrigin);

        var replacementBatch = session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            Array.Empty<ScriptSourceBreakpointRequest>());

        var remainingBreakpoint = Assert.Single(replacementBatch);
        Assert.Equal(branchSite.DebugSiteId, remainingBreakpoint.DebugSiteId);
        Assert.False(remainingBreakpoint.HasSourceOrigin);
        Assert.True(remainingBreakpoint.HasBlueprintOrigin);
        Assert.Single(session.Breakpoints);
    }

    [Fact]
    public void SetBreakpoints_WhenSourceAndBlueprintResolveSameSite_MergesOrigins()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var session = CreateSession(emit);

        session.SetBlueprintBreakpoints(new[] { branchSite.GraphNodeId });
        var batch = session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(12, 9) });

        var breakpoint = Assert.Single(batch);
        Assert.Single(session.Breakpoints);
        Assert.Equal(branchSite.DebugSiteId, breakpoint.DebugSiteId);
        Assert.True(breakpoint.HasSourceOrigin);
        Assert.True(breakpoint.HasBlueprintOrigin);
        Assert.Equal("debug:" + branchSite.DebugSiteId, breakpoint.Key);
    }

    [Fact]
    public void SetBlueprintBreakpoints_WhenReplacingBlueprintSet_DoesNotRemoveSourceOrigin()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var translateSite = FindSite(emit.DebugMap, "Call", "asharia.transform.translate");
        var session = CreateSession(emit);

        session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(14, 13) });
        session.SetBlueprintBreakpoints(new[] { branchSite.GraphNodeId });

        var breakpoints = session.SetBlueprintBreakpoints(Array.Empty<string>());

        var breakpoint = Assert.Single(breakpoints);
        Assert.Equal(translateSite.DebugSiteId, breakpoint.DebugSiteId);
        Assert.True(breakpoint.HasSourceOrigin);
        Assert.False(breakpoint.HasBlueprintOrigin);
    }

    [Fact]
    public void SetSourceBreakpoints_WhenSourcePositionHasNoBlueprintSite_KeepsSourceOnlyBreakpoint()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);

        var batch = session.SetSourceBreakpoints(
            emit.DebugMap.SourceDocumentPath,
            new[] { new ScriptSourceBreakpointRequest(1, 1) });

        var breakpoint = Assert.Single(batch);
        Assert.Equal(ScriptBreakpointBindingStatus.SourceOnly, breakpoint.Status);
        Assert.True(breakpoint.HasSourceOrigin);
        Assert.False(breakpoint.HasBlueprintOrigin);
        Assert.Null(breakpoint.DebugSiteId);
        Assert.Equal(1, breakpoint.Line);
        Assert.Equal(1, breakpoint.Column);
    }

    [Fact]
    public void ResolveStoppedEvent_WhenProbeEventIsNotBreakpoint_ReturnsIgnored()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");

        var stopped = session.ResolveStoppedEvent(
            new DebugRuntimeProbeEvent(Sequence: 1, "Enter", branchProbe.ProbeId, PinId: null, Value: null));

        Assert.Equal(ScriptStoppedEventStatus.Ignored, stopped.Status);
        Assert.Equal(ScriptStoppedReason.Unknown, stopped.Reason);
        Assert.True(stopped.Synthetic);
        Assert.False(stopped.AllThreadsStopped);
        Assert.Null(stopped.DebugSiteId);
    }

    [Fact]
    public void ResolveStoppedProbe_WhenProbeIdIsUnknown_ReturnsUnresolved()
    {
        var emit = EmitPlayerMove();
        var session = CreateSession(emit);

        var stopped = session.ResolveStoppedProbe(-1, synthetic: true);

        Assert.Equal(ScriptStoppedEventStatus.Unresolved, stopped.Status);
        Assert.Equal(ScriptStoppedReason.Breakpoint, stopped.Reason);
        Assert.True(stopped.Synthetic);
        Assert.False(stopped.AllThreadsStopped);
        Assert.Equal(-1, stopped.ProbeId);
        Assert.Null(stopped.DebugSiteId);
    }

    [Fact]
    public void ResolveStoppedProbe_WhenProbeIdIsKnown_ReturnsSourceAndBlueprintSelection()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.DebugSiteId == branchSite.DebugSiteId);
        var session = CreateSession(emit);

        var stopped = session.ResolveStoppedProbe(branchProbe.ProbeId, synthetic: false);

        Assert.Equal(ScriptStoppedEventStatus.Resolved, stopped.Status);
        Assert.Equal(ScriptStoppedReason.Breakpoint, stopped.Reason);
        Assert.False(stopped.Synthetic);
        Assert.True(stopped.AllThreadsStopped);
        Assert.Null(stopped.ThreadId);
        Assert.Equal(branchProbe.ProbeId, stopped.ProbeId);
        Assert.Equal(branchSite.DebugSiteId, stopped.DebugSiteId);
        Assert.Equal(branchSite.GraphNodeId, stopped.GraphNodeId);
        Assert.Equal("Update", stopped.FunctionId);
        Assert.Equal(emit.DebugMap.SourceDocumentPath, stopped.SourcePath);
        Assert.Equal(12, stopped.Line);
        Assert.Equal(9, stopped.Column);
        Assert.NotNull(stopped.PdbSequencePoint);
        Assert.NotNull(stopped.Binding);
    }

    [Fact]
    public void ResolveStoppedProbe_WhenThreadIdIsProvided_RecordsThreadIdForDebuggerStop()
    {
        var emit = EmitPlayerMove();
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");
        var session = CreateSession(emit);

        var stopped = session.ResolveStoppedProbe(branchProbe.ProbeId, synthetic: false, threadId: 11);

        Assert.False(stopped.Synthetic);
        Assert.Equal(11, stopped.ThreadId);
    }

    [Fact]
    public void ReadPausedSnapshot_WhenSyntheticStopIsResolved_ReturnsInspectorScopeOnly()
    {
        var emit = EmitPlayerMove();
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");
        var host = DebugScriptHost.Load(emit);
        host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        host.SetFieldValue(101, "com.game.PlayerMove", "1", "8.5");
        var session = CreateSession(emit);
        var stopped = session.ResolveStoppedProbe(branchProbe.ProbeId, synthetic: true);
        var backend = new FakeFrameVariableBackend(new ScriptFrameVariableSnapshot(
            Available: true,
            Reason: "Should not be read for synthetic stops.",
            Arguments: Array.Empty<ScriptDebugVariable>(),
            Locals: Array.Empty<ScriptDebugVariable>(),
            ThisVariables: Array.Empty<ScriptDebugVariable>()));

        var snapshot = session.ReadPausedSnapshot(host, stopped, entityId: 101, frameVariables: backend);

        Assert.Equal(ScriptPausedSnapshotStatus.Partial, snapshot.Status);
        Assert.True(snapshot.Synthetic);
        Assert.Equal("com.game.PlayerMove", snapshot.BehaviorId);
        Assert.Equal(101, snapshot.EntityId);
        Assert.Same(stopped, snapshot.StoppedEvent);
        Assert.Equal(0, backend.CallCount);

        AssertScopeUnavailable(snapshot, ScriptDebugScopeKind.Arguments);
        AssertScopeUnavailable(snapshot, ScriptDebugScopeKind.Locals);
        AssertScopeUnavailable(snapshot, ScriptDebugScopeKind.This);
        AssertScopeUnavailable(snapshot, ScriptDebugScopeKind.Watch);

        var inspector = Assert.Single(snapshot.Scopes, scope => scope.Kind == ScriptDebugScopeKind.Inspector);
        Assert.True(inspector.Available);
        var speed = Assert.Single(inspector.Variables, variable => variable.FieldId == "1");
        Assert.Equal("Speed", speed.Name);
        Assert.Equal("float", speed.Type);
        Assert.Equal("8.5", speed.DisplayValue);
        Assert.Equal(8.5f, speed.RawValue);
        Assert.Equal("com.game.PlayerMove", speed.BehaviorId);
        Assert.Equal(101, speed.EntityId);
        Assert.Equal("public", speed.Accessibility);
        Assert.Equal("explicit", speed.Serialization);
        Assert.True(speed.Writable);
    }

    [Fact]
    public void ReadPausedSnapshot_WhenStoppedEventIsUnresolved_ReturnsUnavailableScopes()
    {
        var emit = EmitPlayerMove();
        var host = DebugScriptHost.Load(emit);
        var session = CreateSession(emit);
        var stopped = session.ResolveStoppedProbe(-1, synthetic: true);

        var snapshot = session.ReadPausedSnapshot(host, stopped, entityId: 101);

        Assert.Equal(ScriptPausedSnapshotStatus.Unavailable, snapshot.Status);
        Assert.True(snapshot.Synthetic);
        Assert.Equal("com.game.PlayerMove", snapshot.BehaviorId);
        Assert.Equal(101, snapshot.EntityId);
        Assert.All(snapshot.Scopes, scope =>
        {
            Assert.False(scope.Available);
            Assert.Empty(scope.Variables);
            Assert.Contains("not resolved", scope.Reason, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ReadPausedSnapshot_WhenRealStopHasNoFrameBackend_ReturnsUnavailableFrameScopes()
    {
        var emit = EmitPlayerMove();
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");
        var host = DebugScriptHost.Load(emit);
        host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        var session = CreateSession(emit);
        var stopped = session.ResolveStoppedProbe(branchProbe.ProbeId, synthetic: false);

        var snapshot = session.ReadPausedSnapshot(host, stopped, entityId: 101);

        Assert.Equal(ScriptPausedSnapshotStatus.Partial, snapshot.Status);
        Assert.False(snapshot.Synthetic);

        foreach (var kind in new[] { ScriptDebugScopeKind.Arguments, ScriptDebugScopeKind.Locals, ScriptDebugScopeKind.This })
        {
            var scope = Assert.Single(snapshot.Scopes, candidate => candidate.Kind == kind);
            Assert.False(scope.Available);
            Assert.Empty(scope.Variables);
            Assert.Contains("backend is not connected", scope.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReadPausedSnapshot_WhenRealStopHasFrameBackend_ReturnsFrameScopes()
    {
        var emit = EmitPlayerMove();
        var branchProbe = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");
        var host = DebugScriptHost.Load(emit);
        host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        var session = CreateSession(emit);
        var stopped = session.ResolveStoppedProbe(branchProbe.ProbeId, synthetic: false);
        var backend = new FakeFrameVariableBackend(new ScriptFrameVariableSnapshot(
            Available: true,
            Reason: "Frame variables read from debugger backend.",
            Arguments: new[]
            {
                new ScriptDebugVariable(
                    Name: "delta",
                    VariableId: "frame:arg:delta",
                    Type: "float",
                    DisplayValue: "0.016",
                    RawValue: 0.016f,
                    BehaviorId: null,
                    EntityId: null,
                    FieldId: null,
                    Accessibility: null,
                    Serialization: null,
                    Writable: false)
            },
            Locals: new[]
            {
                new ScriptDebugVariable(
                    Name: "amount",
                    VariableId: "frame:local:amount",
                    Type: "float",
                    DisplayValue: "0.128",
                    RawValue: 0.128f,
                    BehaviorId: null,
                    EntityId: null,
                    FieldId: null,
                    Accessibility: null,
                    Serialization: null,
                    Writable: false)
            },
            ThisVariables: new[]
            {
                new ScriptDebugVariable(
                    Name: "Speed",
                    VariableId: "frame:this:Speed",
                    Type: "float",
                    DisplayValue: "8.5",
                    RawValue: 8.5f,
                    BehaviorId: "com.game.PlayerMove",
                    EntityId: 101,
                    FieldId: "Speed",
                    Accessibility: "public",
                    Serialization: "public",
                    Writable: false)
            }));

        var snapshot = session.ReadPausedSnapshot(host, stopped, entityId: 101, frameVariables: backend);

        Assert.Equal(1, backend.CallCount);
        Assert.Same(stopped, backend.LastStoppedEvent);

        var arguments = Assert.Single(snapshot.Scopes, scope => scope.Kind == ScriptDebugScopeKind.Arguments);
        Assert.True(arguments.Available);
        var delta = Assert.Single(arguments.Variables);
        Assert.Equal("delta", delta.Name);
        Assert.Equal("float", delta.Type);
        Assert.Equal("0.016", delta.DisplayValue);

        var locals = Assert.Single(snapshot.Scopes, scope => scope.Kind == ScriptDebugScopeKind.Locals);
        Assert.True(locals.Available);
        var amount = Assert.Single(locals.Variables);
        Assert.Equal("amount", amount.Name);
        Assert.Equal(0.128f, amount.RawValue);

        var thisScope = Assert.Single(snapshot.Scopes, scope => scope.Kind == ScriptDebugScopeKind.This);
        Assert.True(thisScope.Available);
        var speed = Assert.Single(thisScope.Variables);
        Assert.Equal("Speed", speed.Name);
        Assert.Equal("frame:this:Speed", speed.VariableId);
        Assert.False(speed.Writable);
    }

    [Fact]
    public void RecordWatchValues_WhenValueProbeEventsExist_ReturnsLatestValuesWithDebugMapMetadata()
    {
        var emit = EmitDebugWatch();
        var amountSite = FindSite(emit.DebugMap, "Watch", "amount");
        var offsetSite = FindSite(emit.DebugMap, "Watch", "offset");
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 9, "com.game.DebugWatch");
        var session = CreateSession(emit);

        host.SetWatchEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        instance.SetFieldValue("1", 8.0f);
        instance.InvokeUpdate(0.016f);

        var variables = session.RecordWatchValues(host.GetProbeEvents());

        Assert.Equal(2, variables.Count);
        var amount = Assert.Single(variables, variable => variable.Name == "amount");
        Assert.Equal("watch:" + amountSite.DebugSiteId + ":amount", amount.VariableId);
        Assert.Equal("float", amount.Type);
        Assert.Equal("0.128", amount.DisplayValue);
        Assert.Equal(0.128f, amount.RawValue);
        Assert.Equal(amountSite.DebugSiteId, amount.DebugSiteId);
        Assert.Equal(amountSite.GraphNodeId, amount.GraphNodeId);
        Assert.Equal("Update", amount.FunctionId);
        Assert.Equal(amountSite.ProbeId, amount.ProbeId);
        Assert.Equal("amount", amount.PinId);
        Assert.Equal(3, amount.Sequence);
        Assert.Equal(2, amount.HitCount);
        Assert.False(amount.Writable);

        var offset = Assert.Single(variables, variable => variable.Name == "offset");
        Assert.Equal(offsetSite.DebugSiteId, offset.DebugSiteId);
        Assert.Equal(offsetSite.GraphNodeId, offset.GraphNodeId);
        Assert.Equal(offsetSite.ProbeId, offset.ProbeId);
        Assert.Equal("offset", offset.PinId);
        Assert.Equal(4, offset.Sequence);
        Assert.Equal(2, offset.HitCount);
        Assert.Contains("Vec3", offset.Type, StringComparison.Ordinal);
        Assert.Contains("0.128", offset.DisplayValue, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadPausedSnapshot_WhenWatchValuesWereRecorded_ReturnsWatchScope()
    {
        var emit = EmitDebugWatch();
        var offsetSite = FindSite(emit.DebugMap, "Watch", "offset");
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 9, "com.game.DebugWatch");
        var session = CreateSession(emit);

        host.SetWatchEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        session.RecordWatchValues(host.GetProbeEvents());
        var stopped = session.ResolveStoppedProbe(offsetSite.ProbeId!.Value, synthetic: true);

        var snapshot = session.ReadPausedSnapshot(host, stopped, entityId: 9);

        var watch = Assert.Single(snapshot.Scopes, scope => scope.Kind == ScriptDebugScopeKind.Watch);
        Assert.True(watch.Available);
        Assert.Equal(2, watch.Variables.Count);
        Assert.Contains(watch.Variables, variable =>
            variable.Name == "amount" &&
            variable.DisplayValue == "0.064");
        Assert.Contains(watch.Variables, variable =>
            variable.Name == "offset" &&
            variable.DisplayValue.Contains("0.064", StringComparison.Ordinal));
    }

    [Fact]
    public void ClearWatchValues_WhenValuesWereRecorded_RemovesWatchScopeVariables()
    {
        var emit = EmitDebugWatch();
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 9, "com.game.DebugWatch");
        var session = CreateSession(emit);

        host.SetWatchEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        Assert.NotEmpty(session.RecordWatchValues(host.GetProbeEvents()));

        session.ClearWatchValues();
        var stopped = session.ResolveStoppedProbe(
            Assert.Single(emit.ProbeSites, site => site.Kind == "Watch" && site.Label == "offset").ProbeId,
            synthetic: true);
        var snapshot = session.ReadPausedSnapshot(host, stopped, entityId: 9);

        AssertScopeUnavailable(snapshot, ScriptDebugScopeKind.Watch);
    }

    [Fact]
    public void RecordTraceEvents_WhenEnterProbeEventsExist_AggregatesByDebugSiteId()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        var session = CreateSession(emit);

        host.SetTraceEnabled(true);
        host.SetBreakpointByDebugSiteId(branchSite.DebugSiteId, enabled: true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        instance.InvokeUpdate(0.016f);

        var snapshot = session.RecordTraceEvents(host.GetProbeEvents());

        Assert.Equal(2, snapshot.Sequence);
        Assert.Equal(256, snapshot.RecentCapacity);
        Assert.Equal(0, snapshot.DroppedSampleCount);
        var branch = Assert.Single(snapshot.Sites);
        Assert.Equal(branchSite.DebugSiteId, branch.DebugSiteId);
        Assert.Equal(branchSite.GraphNodeId, branch.GraphNodeId);
        Assert.Equal("Update", branch.FunctionId);
        Assert.Equal("Branch", branch.Kind);
        Assert.Equal("Branch", branch.Label);
        Assert.Equal(branchSite.ProbeId, branch.ProbeId);
        Assert.Equal(2, branch.HitCount);
        Assert.Equal(2, branch.LastSequence);
        Assert.Equal(2, snapshot.RecentSamples.Count);
        Assert.All(snapshot.RecentSamples, sample =>
        {
            Assert.Equal(branchSite.DebugSiteId, sample.DebugSiteId);
            Assert.Equal(branchSite.GraphNodeId, sample.GraphNodeId);
            Assert.Equal(branchSite.ProbeId!.Value, sample.ProbeId);
            Assert.Equal("Branch", sample.Kind);
        });
    }

    [Fact]
    public void RecordTraceEvents_WhenBufferCapacityIsExceeded_DropsOldSamplesButKeepsHitCounts()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        var session = CreateSession(emit, traceSampleCapacity: 1);

        host.SetTraceEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        instance.InvokeUpdate(0.016f);

        var snapshot = session.RecordTraceEvents(host.GetProbeEvents());

        Assert.Equal(2, snapshot.Sequence);
        Assert.Equal(1, snapshot.RecentCapacity);
        Assert.Equal(1, snapshot.DroppedSampleCount);
        Assert.Single(snapshot.Sites, site =>
            site.DebugSiteId == branchSite.DebugSiteId &&
            site.HitCount == 2 &&
            site.LastSequence == 2);
        var sample = Assert.Single(snapshot.RecentSamples);
        Assert.Equal(2, sample.Sequence);
        Assert.Equal(branchSite.DebugSiteId, sample.DebugSiteId);
    }

    [Fact]
    public void RecordTraceEvents_WhenOnlyValueProbeEventsExist_IgnoresEvents()
    {
        var emit = EmitDebugWatch();
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 9, "com.game.DebugWatch");
        var session = CreateSession(emit);

        host.SetWatchEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);

        var snapshot = session.RecordTraceEvents(host.GetProbeEvents());

        Assert.Equal(0, snapshot.Sequence);
        Assert.Empty(snapshot.Sites);
        Assert.Empty(snapshot.RecentSamples);
        Assert.Equal(0, snapshot.DroppedSampleCount);
    }

    [Fact]
    public void ClearTrace_WhenTraceExists_RemovesSitesAndRecentSamples()
    {
        var emit = EmitPlayerMove();
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        var session = CreateSession(emit);

        host.SetTraceEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        Assert.NotEmpty(session.RecordTraceEvents(host.GetProbeEvents()).Sites);

        session.ClearTrace();
        var snapshot = session.GetTraceSnapshot();

        Assert.Equal(0, snapshot.Sequence);
        Assert.Empty(snapshot.Sites);
        Assert.Empty(snapshot.RecentSamples);
        Assert.Equal(0, snapshot.DroppedSampleCount);
    }

    [Fact]
    public void IngestProbeEvents_WhenSameTraceLogIsPolledTwice_DoesNotDoubleCount()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        var session = CreateSession(emit);

        host.SetTraceEnabled(true);
        session.SetTraceObservationEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        var first = session.IngestProbeEvents(host.GetProbeEvents());
        var second = session.IngestProbeEvents(host.GetProbeEvents());

        Assert.Equal(1, first.ProcessedCount);
        Assert.Equal(1, first.Cursor);
        Assert.Equal(1, first.TraceSnapshot.Sequence);
        Assert.Empty(first.StoppedEvents);
        Assert.Single(first.TraceSnapshot.Sites, site =>
            site.DebugSiteId == branchSite.DebugSiteId &&
            site.HitCount == 1);

        Assert.Equal(0, second.ProcessedCount);
        Assert.Equal(1, second.Cursor);
        Assert.Equal(1, second.TraceSnapshot.Sequence);
        Assert.Single(second.TraceSnapshot.Sites, site =>
            site.DebugSiteId == branchSite.DebugSiteId &&
            site.HitCount == 1);
    }

    [Fact]
    public void IngestProbeEvents_WhenTraceObserverIsDisabled_DoesNotAggregateTrace()
    {
        var emit = EmitPlayerMove();
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        var session = CreateSession(emit);

        host.SetTraceEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);

        var ingest = session.IngestProbeEvents(host.GetProbeEvents());

        Assert.Equal(1, ingest.ProcessedCount);
        Assert.Equal(0, ingest.TraceSnapshot.Sequence);
        Assert.Empty(ingest.TraceSnapshot.Sites);
    }

    [Fact]
    public void IngestProbeEvents_WhenSameWatchLogIsPolledTwice_DoesNotDoubleCount()
    {
        var emit = EmitDebugWatch();
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 9, "com.game.DebugWatch");
        var session = CreateSession(emit);

        host.SetWatchEnabled(true);
        session.SetWatchObservationEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        var first = session.IngestProbeEvents(host.GetProbeEvents());
        var second = session.IngestProbeEvents(host.GetProbeEvents());

        Assert.Equal(2, first.ProcessedCount);
        var amount = Assert.Single(first.WatchVariables, variable => variable.Name == "amount");
        Assert.Equal(1, amount.HitCount);
        Assert.Equal(1, amount.Sequence);
        Assert.Equal("0.064", amount.DisplayValue);

        Assert.Equal(0, second.ProcessedCount);
        amount = Assert.Single(second.WatchVariables, variable => variable.Name == "amount");
        Assert.Equal(1, amount.HitCount);
        Assert.Equal(1, amount.Sequence);
        Assert.Equal("0.064", amount.DisplayValue);
    }

    [Fact]
    public void IngestProbeEvents_WhenWatchObserverIsDisabled_DoesNotAggregateWatchValues()
    {
        var emit = EmitDebugWatch();
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 9, "com.game.DebugWatch");
        var session = CreateSession(emit);

        host.SetWatchEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);

        var ingest = session.IngestProbeEvents(host.GetProbeEvents());

        Assert.Equal(2, ingest.ProcessedCount);
        Assert.Empty(ingest.WatchVariables);
    }

    [Fact]
    public void IngestProbeEvents_WhenBreakpointEventExists_ReturnsStoppedEventsOnce()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        var session = CreateSession(emit);
        host.SetBreakpointByDebugSiteId(branchSite.DebugSiteId, enabled: true);

        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        var first = session.IngestProbeEvents(host.GetProbeEvents());
        var second = session.IngestProbeEvents(host.GetProbeEvents());

        var stopped = Assert.Single(first.StoppedEvents);
        Assert.Equal(ScriptStoppedEventStatus.Resolved, stopped.Status);
        Assert.Equal(branchSite.DebugSiteId, stopped.DebugSiteId);
        Assert.True(stopped.Synthetic);
        Assert.Equal(1, first.ProcessedCount);
        Assert.Empty(second.StoppedEvents);
        Assert.Equal(0, second.ProcessedCount);
    }

    [Fact]
    public void IngestProbeEvents_WhenHostEventsWereCleared_ResetsCursorForNewLog()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        var session = CreateSession(emit);

        host.SetTraceEnabled(true);
        session.SetTraceObservationEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        var first = session.IngestProbeEvents(host.GetProbeEvents());
        Assert.Equal(1, first.ProcessedCount);

        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        var second = session.IngestProbeEvents(host.GetProbeEvents());

        Assert.Equal(1, second.ProcessedCount);
        Assert.Equal(1, second.Cursor);
        Assert.Equal(2, second.TraceSnapshot.Sequence);
        Assert.Single(second.TraceSnapshot.Sites, site =>
            site.DebugSiteId == branchSite.DebugSiteId &&
            site.HitCount == 2);
    }

    [Fact]
    public void ResetProbeEventCursor_WhenCalled_AllowsExplicitReplay()
    {
        var emit = EmitPlayerMove();
        var branchSite = FindSite(emit.DebugMap, "Branch", "Branch");
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        var session = CreateSession(emit);

        host.SetTraceEnabled(true);
        session.SetTraceObservationEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);
        session.IngestProbeEvents(host.GetProbeEvents());

        session.ResetProbeEventCursor();
        var replay = session.IngestProbeEvents(host.GetProbeEvents());

        Assert.Equal(1, replay.ProcessedCount);
        Assert.Equal(2, replay.TraceSnapshot.Sequence);
        Assert.Single(replay.TraceSnapshot.Sites, site =>
            site.DebugSiteId == branchSite.DebugSiteId &&
            site.HitCount == 2);
    }

    private static void AssertScopeUnavailable(ScriptPausedSnapshot snapshot, string kind)
    {
        var scope = Assert.Single(snapshot.Scopes, candidate => candidate.Kind == kind);
        Assert.False(scope.Available);
        Assert.Empty(scope.Variables);
    }

    private sealed class FakeFrameVariableBackend : IScriptFrameVariableBackend
    {
        private readonly ScriptFrameVariableSnapshot snapshot;

        public FakeFrameVariableBackend(ScriptFrameVariableSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public int CallCount { get; private set; }

        public ScriptStoppedEvent? LastStoppedEvent { get; private set; }

        public ScriptFrameVariableSnapshot ReadVariables(ScriptStoppedEvent stoppedEvent)
        {
            CallCount++;
            LastStoppedEvent = stoppedEvent;
            return snapshot;
        }
    }

    private static ScriptDebugSession CreateSession(DebugScriptEmitResult emit, int traceSampleCapacity = 256)
    {
        return new ScriptDebugSession(
            emit.DebugMap,
            File.ReadAllText(emit.DebugMap.SourceDocumentPath),
            emit.DebugMap.SourceDocumentPath,
            traceSampleCapacity);
    }

    private static DebugScriptEmitResult EmitPlayerMove()
    {
        return DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            Path.Combine(
                Path.GetTempPath(),
                "ScriptLab.Tests",
                Guid.NewGuid().ToString("N")));
    }

    private static DebugScriptEmitResult EmitDebugWatch()
    {
        return DebugScriptCompiler.EmitFile(
            GetSamplePath("DebugWatch.ash.cs"),
            Path.Combine(
                Path.GetTempPath(),
                "ScriptLab.Tests",
                Guid.NewGuid().ToString("N")));
    }

    private static ScriptDebugMapSite FindSite(ScriptDebugMap debugMap, string kind, string label)
    {
        return debugMap.Functions
            .SelectMany(function => function.Sites)
            .Single(site => site.Kind == kind && site.Label == label);
    }

    private static string GetSamplePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Samples", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate sample script '{fileName}'.");
    }
}
