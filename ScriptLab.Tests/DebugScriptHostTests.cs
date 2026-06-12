using ScriptLab;

namespace ScriptLab.Tests;

public sealed class DebugScriptHostTests
{
    [Fact]
    public void MountBehavior_WhenPublicFieldExists_ReadsAndWritesFieldByEntityBehaviorAndFieldId()
    {
        var outputDirectory = CreateOutputDirectory();
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            outputDirectory);
        var host = DebugScriptHost.Load(emit);

        var instance = host.MountBehavior(entityId: 101, "com.game.PlayerMove");
        var speed = Assert.Single(instance.GetFields(), field => field.FieldId == new FieldId(1));

        Assert.Equal(101, speed.EntityId);
        Assert.Equal("com.game.PlayerMove", speed.BehaviorId);
        Assert.Equal("Speed", speed.Name);
        Assert.Equal("float", speed.Type);
        Assert.Equal("public", speed.Accessibility);
        Assert.Equal("explicit", speed.Serialization);
        Assert.Equal(4.0f, speed.Value);

        host.SetFieldValue(101, "com.game.PlayerMove", "1", "8.5");

        Assert.Equal(8.5f, host.GetFieldValue(101, "com.game.PlayerMove", "1"));
    }

    [Fact]
    public void MountBehavior_WhenPrivateFieldIsExplicit_ReadsAndWritesField()
    {
        var outputDirectory = CreateOutputDirectory();
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("PrivateSerializedField.ash.cs"),
            outputDirectory);
        var host = DebugScriptHost.Load(emit);

        var instance = host.MountBehavior(entityId: 7, "com.game.PrivateSerializedField");
        var speed = Assert.Single(instance.GetFields());

        Assert.Equal(new FieldId(1), speed.FieldId);
        Assert.Equal("float", speed.Type);
        Assert.Equal("private", speed.Accessibility);
        Assert.Equal("explicit", speed.Serialization);
        Assert.Equal(4.0f, speed.Value);

        instance.SetFieldValue("1", 2.25f);

        Assert.Equal(2.25f, instance.GetFieldValue("1"));
    }

    [Fact]
    public void SetBreakpointByDebugSiteId_WhenUpdateRuns_RecordsBreakpointEventWithoutRecompile()
    {
        var outputDirectory = CreateOutputDirectory();
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            outputDirectory);
        var branchSite = Assert.Single(emit.ProbeSites, site => site.Kind == "Branch");
        var assemblyLastWrite = File.GetLastWriteTimeUtc(emit.AssemblyPath);
        var instrumentedSourceLastWrite = File.GetLastWriteTimeUtc(emit.InstrumentedSourcePath);
        var host = DebugScriptHost.Load(emit);

        host.SetBreakpointByDebugSiteId(branchSite.DebugSiteId, enabled: true);

        Assert.Contains(branchSite.ProbeId, host.GetBreakpointProbeIds());
        Assert.Equal(assemblyLastWrite, File.GetLastWriteTimeUtc(emit.AssemblyPath));
        Assert.Equal(instrumentedSourceLastWrite, File.GetLastWriteTimeUtc(emit.InstrumentedSourcePath));

        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);

        Assert.Contains(host.GetProbeEvents(), probeEvent =>
            probeEvent.Kind == "Breakpoint" &&
            probeEvent.ProbeId == branchSite.ProbeId);

        host.SetBreakpoint(branchSite.ProbeId, enabled: false);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);

        Assert.DoesNotContain(host.GetProbeEvents(), probeEvent =>
            probeEvent.Kind == "Breakpoint" &&
            probeEvent.ProbeId == branchSite.ProbeId);
    }

    [Fact]
    public void SetBreakpoint_WhenWatchValueRuns_RecordsBreakpointEvent()
    {
        var outputDirectory = CreateOutputDirectory();
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("DebugWatch.ash.cs"),
            outputDirectory);
        var amountSite = Assert.Single(emit.ProbeSites, site => site.Kind == "Watch" && site.Label == "amount");
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.DebugWatch");

        host.SetBreakpoint(amountSite.ProbeId, enabled: true);
        host.SetWatchEnabled(true);
        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);

        Assert.Contains(host.GetProbeEvents(), probeEvent =>
            probeEvent.Kind == "Value" &&
            probeEvent.ProbeId == amountSite.ProbeId &&
            probeEvent.PinId == "amount");
        Assert.Contains(host.GetProbeEvents(), probeEvent =>
            probeEvent.Kind == "Breakpoint" &&
            probeEvent.ProbeId == amountSite.ProbeId);
    }

    [Fact]
    public void InvokeUpdate_WhenTraceObservationIsDisabled_DoesNotRecordEnterEvents()
    {
        var outputDirectory = CreateOutputDirectory();
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            outputDirectory);
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.PlayerMove");

        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);

        Assert.Empty(host.GetProbeEvents());
    }

    [Fact]
    public void InvokeUpdate_WhenWatchObservationIsDisabled_DoesNotRecordValueEvents()
    {
        var outputDirectory = CreateOutputDirectory();
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("DebugWatch.ash.cs"),
            outputDirectory);
        var host = DebugScriptHost.Load(emit);
        var instance = host.MountBehavior(entityId: 1, "com.game.DebugWatch");

        host.ClearProbeEvents();
        instance.InvokeUpdate(0.016f);

        Assert.Empty(host.GetProbeEvents());
    }

    private static string CreateOutputDirectory()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            Guid.NewGuid().ToString("N"));
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
