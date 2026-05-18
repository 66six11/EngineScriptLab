using System.Reflection;
using System.Text.Json;
using ScriptLab;

namespace ScriptLab.Tests;

public sealed class DebugScriptCompilerTests
{
    [Fact]
    public void EmitFile_WhenPlayerMoveIsValid_EmitsInstrumentedAssemblyAndPdb()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            Guid.NewGuid().ToString("N"));

        var result = DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            outputDirectory);

        Assert.True(File.Exists(result.AssemblyPath));
        Assert.True(File.Exists(result.PdbPath));
        Assert.True(File.Exists(result.InstrumentedSourcePath));
        Assert.True(File.Exists(result.ProbeManifestPath));
        Assert.True(File.Exists(result.DebugMapPath));
        Assert.Equal("com.game.PlayerMove", result.DebugMap.BehaviorId);
        Assert.Equal(Path.GetFullPath(GetSamplePath("PlayerMove.ash.cs")), result.DebugMap.SourceDocumentPath);
        Assert.Equal(64, result.DebugMap.SourceChecksum.Length);
        Assert.True(Guid.TryParse(result.DebugMap.AssemblyMvid, out _));
        Assert.NotEmpty(result.DebugMap.PdbId);

        var branchSite = Assert.Single(result.ProbeSites, site => site.Kind == "Branch");
        var translateSite = Assert.Single(result.ProbeSites, site => site.Label == "asharia.transform.translate");
        Assert.True(branchSite.ProbeId > 0);
        Assert.StartsWith("ds_", branchSite.DebugSiteId);
        Assert.Equal(BehaviorIrBreakabilityHint.Breakable, branchSite.BreakabilityHint);
        Assert.False(branchSite.BreakableVerified);
        Assert.Equal("n3", branchSite.GraphNodeId);
        var instrumentedSource = File.ReadAllText(result.InstrumentedSourcePath);
        Assert.Contains("#line hidden", instrumentedSource);
        Assert.Contains("#line 12", instrumentedSource);
        Assert.Contains($"DebugProbe.Enter({branchSite.ProbeId})", instrumentedSource);
        Assert.Contains($"DebugProbe.Enter({translateSite.ProbeId})", instrumentedSource);

        var manifestJson = File.ReadAllText(result.ProbeManifestPath);
        Assert.Contains($@"""probeId"": {branchSite.ProbeId}", manifestJson);
        Assert.Contains($@"""debugSiteId"": ""{branchSite.DebugSiteId}""", manifestJson);
        Assert.Contains($@"""graphNodeId"": ""{branchSite.GraphNodeId}""", manifestJson);

        var updateMap = Assert.Single(result.DebugMap.Functions);
        Assert.Equal("Update", updateMap.FunctionId);
        var branchMap = Assert.Single(updateMap.Sites, site => site.DebugSiteId == branchSite.DebugSiteId);
        Assert.Equal(branchSite.GraphNodeId, branchMap.GraphNodeId);
        Assert.Equal(branchSite.ProbeId, branchMap.ProbeId);
        Assert.Equal(BehaviorIrBreakabilityHint.Breakable, branchMap.BreakabilityHint);
        Assert.True(branchMap.BreakableVerified);
        Assert.NotNull(branchMap.MethodToken);
        Assert.NotNull(branchMap.IlOffset);
        Assert.NotNull(branchMap.PdbSequencePoint);
        Assert.Equal(12, branchMap.PdbSequencePoint.StartLine);
        Assert.Equal(9, branchMap.PdbSequencePoint.StartColumn);
        Assert.NotNull(branchMap.LocalScope);
        Assert.NotEmpty(branchMap.SourceTextHash);
        var translateMap = Assert.Single(updateMap.Sites, site => site.DebugSiteId == translateSite.DebugSiteId);
        Assert.True(translateMap.BreakableVerified);
        Assert.Equal(14, translateMap.PdbSequencePoint?.StartLine);
        Assert.Equal(13, translateMap.PdbSequencePoint?.StartColumn);
        var multiplyMap = Assert.Single(updateMap.Sites, site => site.Kind == "BinaryOp" && site.Label == "Multiply");
        Assert.Equal(BehaviorIrBreakabilityHint.Observable, multiplyMap.BreakabilityHint);
        Assert.False(multiplyMap.BreakableVerified);
        Assert.Null(multiplyMap.ProbeId);
        Assert.Null(multiplyMap.PdbSequencePoint);
        Assert.NotNull(multiplyMap.OwningBreakableDebugSiteId);

        using var debugMapDocument = JsonDocument.Parse(File.ReadAllText(result.DebugMapPath));
        var debugMapRoot = debugMapDocument.RootElement;
        Assert.Equal(1, debugMapRoot.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(result.DebugMap.BuildId, debugMapRoot.GetProperty("buildId").GetString());
        Assert.Equal(result.DebugMap.AssemblyMvid, debugMapRoot.GetProperty("assemblyMvid").GetString());
        Assert.Equal(result.DebugMap.PdbId, debugMapRoot.GetProperty("pdbId").GetString());
    }

    [Fact]
    public void EmittedAssembly_WhenUpdateRuns_RecordsProbeEvents()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            Guid.NewGuid().ToString("N"));
        var result = DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            outputDirectory);
        var branchSite = Assert.Single(result.ProbeSites, site => site.Kind == "Branch");
        var translateSite = Assert.Single(result.ProbeSites, site => site.Label == "asharia.transform.translate");
        var assembly = Assembly.Load(
            File.ReadAllBytes(result.AssemblyPath),
            File.ReadAllBytes(result.PdbPath));

        var debugProbe = assembly.GetType("Asharia.Behavior.DebugProbe", throwOnError: true)!;
        debugProbe.GetMethod("Clear")!.Invoke(null, null);
        debugProbe.GetMethod("SetTraceEnabled")!.Invoke(null, new object[] { true });

        var keyType = assembly.GetType("Asharia.Behavior.Key", throwOnError: true)!;
        var input = assembly.GetType("Asharia.Behavior.Input", throwOnError: true)!;
        input.GetMethod("SetKeyDown")!.Invoke(
            null,
            new[] { Enum.Parse(keyType, "W"), true });

        var behaviorType = assembly.GetType("com.game.PlayerMove", throwOnError: true)!;
        var behavior = Activator.CreateInstance(behaviorType)!;
        behaviorType
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(behavior, new object[] { 0.016f });

        var events = ((System.Collections.IEnumerable)debugProbe.GetProperty("Events")!.GetValue(null)!)
            .Cast<object>()
            .ToArray();
        var enteredProbeIds = events
            .Where(probeEvent => GetStringProperty(probeEvent, "Kind") == "Enter")
            .Select(probeEvent => GetIntProperty(probeEvent, "ProbeId"))
            .ToArray();

        Assert.Contains(branchSite.ProbeId, enteredProbeIds);
        Assert.Contains(translateSite.ProbeId, enteredProbeIds);
    }

    [Fact]
    public void EmittedAssembly_WhenGraphDebugWatchRuns_RecordsValueProbeEvents()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            Guid.NewGuid().ToString("N"));
        var result = DebugScriptCompiler.EmitFile(
            GetSamplePath("DebugWatch.ash.cs"),
            outputDirectory);
        var amountSite = Assert.Single(result.ProbeSites, site => site.Kind == "Watch" && site.Label == "amount");
        var offsetSite = Assert.Single(result.ProbeSites, site => site.Kind == "Watch" && site.Label == "offset");
        var assembly = Assembly.Load(
            File.ReadAllBytes(result.AssemblyPath),
            File.ReadAllBytes(result.PdbPath));

        var debugProbe = assembly.GetType("Asharia.Behavior.DebugProbe", throwOnError: true)!;
        debugProbe.GetMethod("Clear")!.Invoke(null, null);
        debugProbe.GetMethod("SetWatchEnabled")!.Invoke(null, new object[] { true });

        var behaviorType = assembly.GetType("com.game.DebugWatch", throwOnError: true)!;
        var behavior = Activator.CreateInstance(behaviorType)!;
        behaviorType
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(behavior, new object[] { 0.016f });

        var events = ((System.Collections.IEnumerable)debugProbe.GetProperty("Events")!.GetValue(null)!)
            .Cast<object>()
            .ToArray();
        var valueEvents = events
            .Where(probeEvent => GetStringProperty(probeEvent, "Kind") == "Value")
            .ToArray();

        Assert.Contains(valueEvents, probeEvent =>
            GetIntProperty(probeEvent, "ProbeId") == amountSite.ProbeId &&
            GetStringProperty(probeEvent, "PinId") == "amount" &&
            AssertValue<float>(probeEvent, 0.064f));
        Assert.Contains(valueEvents, probeEvent =>
            GetIntProperty(probeEvent, "ProbeId") == offsetSite.ProbeId &&
            GetStringProperty(probeEvent, "PinId") == "offset");
    }

    private static string GetStringProperty(object instance, string name)
    {
        return (string)instance.GetType().GetProperty(name)!.GetValue(instance)!;
    }

    private static int GetIntProperty(object instance, string name)
    {
        return (int)instance.GetType().GetProperty(name)!.GetValue(instance)!;
    }

    private static bool AssertValue<T>(object instance, T expected)
    {
        Assert.Equal(expected, (T)instance.GetType().GetProperty("Value")!.GetValue(instance)!);
        return true;
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
