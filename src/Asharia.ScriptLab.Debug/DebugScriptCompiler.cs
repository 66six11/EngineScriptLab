using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ScriptLab;

public sealed record DebugScriptEmitResult(
    string AssemblyPath,
    string PdbPath,
    string InstrumentedSourcePath,
    string ProbeManifestPath,
    string DebugMapPath,
    DebugProbeManifest ProbeManifest,
    ScriptDebugMap DebugMap)
{
    public IReadOnlyList<DebugProbeSite> ProbeSites => ProbeManifest.Probes;
}

public sealed record DebugProbeManifest(
    string BehaviorId,
    string SourcePath,
    IReadOnlyList<DebugProbeSite> Probes);

public sealed record DebugProbeSite(
    int ProbeId,
    string DebugSiteId,
    string GraphNodeId,
    string Kind,
    string Label,
    BehaviorSourceSpan Source,
    string BreakabilityHint,
    bool BreakableVerified,
    IReadOnlyList<DebugProbePin> Pins);

public sealed record DebugProbePin(string PinId, string Name);

public sealed record ScriptDebugMap(
    int SchemaVersion,
    string BuildId,
    string AssemblyMvid,
    string PdbId,
    string SourceDocumentPath,
    string SourceChecksum,
    string BehaviorId,
    IReadOnlyList<ScriptDebugMapFunction> Functions);

public sealed record ScriptDebugMapFunction(
    string FunctionId,
    IReadOnlyList<ScriptDebugMapSite> Sites);

public sealed record ScriptDebugMapSite(
    string DebugSiteId,
    string IrInstructionId,
    BehaviorSourceSpan SourceSpan,
    string SourceTextHash,
    string GraphNodeId,
    string Kind,
    string Label,
    string BreakabilityHint,
    bool BreakableVerified,
    bool Observable,
    string? OwningBreakableDebugSiteId,
    int? ProbeId,
    string? MethodToken,
    int? IlOffset,
    int? IlOffsetEnd,
    ScriptDebugMapSequencePoint? PdbSequencePoint,
    ScriptDebugMapLocalScope? LocalScope);

public sealed record ScriptDebugMapSequencePoint(
    string DocumentPath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

public sealed record ScriptDebugMapLocalScope(
    string MethodToken,
    int StartOffset,
    int Length,
    IReadOnlyList<string> LocalNames);

internal sealed record PortablePdbSequencePointInfo(
    string MethodToken,
    int Offset,
    int? EndOffset,
    string DocumentPath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

internal sealed record PortablePdbLocalScopeInfo(
    string MethodToken,
    int StartOffset,
    int Length,
    IReadOnlyList<string> LocalNames);

public static class SourceInstrumentedDebugCompiler
{
    private const string DebugProbeSourcePath = "Asharia.DebugProbe.g.cs";
    private const string GlobalUsingsSourcePath = "Asharia.DebugGlobals.g.cs";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static DebugScriptEmitResult EmitFile(string scriptPath, string outputDirectory)
    {
        var fullPath = Path.GetFullPath(scriptPath);
        var source = File.ReadAllText(fullPath);
        var module = BehaviorIrLowerer.LowerFile(fullPath);
        var graph = BlueprintGraphProjector.Project(module);
        var candidateManifest = DebugProbeManifestFactory.Create(module.BehaviorId, fullPath, graph);
        var scriptTree = ParseSource(source, fullPath);
        var root = scriptTree.GetCompilationUnitRoot();
        var rewriter = new DebugInstrumentationRewriter(candidateManifest);
        var instrumentedRoot = (CompilationUnitSyntax)rewriter.Visit(root)!;
        var instrumentedSource = instrumentedRoot.ToFullString();

        Directory.CreateDirectory(outputDirectory);

        var assemblyName = $"{SanitizeAssemblyName(module.BehaviorId)}.Debug";
        var assemblyPath = Path.Combine(outputDirectory, $"{assemblyName}.dll");
        var pdbPath = Path.Combine(outputDirectory, $"{assemblyName}.pdb");
        var instrumentedSourcePath = Path.Combine(outputDirectory, $"{assemblyName}.Debug.g.cs");
        var probeManifestPath = Path.Combine(outputDirectory, $"{assemblyName}.probe.json");
        var debugMapPath = Path.Combine(outputDirectory, $"{assemblyName}.debugmap.json");

        File.WriteAllText(instrumentedSourcePath, instrumentedSource, Encoding.UTF8);

        var syntaxTrees = new[]
        {
            ParseSource(GetGlobalUsingsSource(), GlobalUsingsSourcePath),
            ParseSource(
                File.ReadAllText(ScriptPathResolver.Resolve(Path.Combine("Mock", "AshariaBehavior.cs"))),
                ScriptPathResolver.Resolve(Path.Combine("Mock", "AshariaBehavior.cs"))),
            ParseSource(GetDebugProbeSource(), DebugProbeSourcePath),
            ParseSource(instrumentedSource, fullPath)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            GetPlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: false));

        EmitResult emitResult;
        using (var assemblyStream = File.Create(assemblyPath))
        using (var pdbStream = File.Create(pdbPath))
        {
            emitResult = compilation.Emit(
                assemblyStream,
                pdbStream: pdbStream,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        }

        if (!emitResult.Success)
        {
            throw new InvalidOperationException(
                "Debug script compilation failed:" + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(diagnostic => diagnostic.ToString())));
        }

        var emittedManifest = candidateManifest with
        {
            Probes = rewriter.ProbeSites
        };
        File.WriteAllText(
            probeManifestPath,
            JsonSerializer.Serialize(emittedManifest, ManifestJsonOptions),
            Encoding.UTF8);

        var debugMap = ScriptDebugMapFactory.Create(
            assemblyPath,
            pdbPath,
            fullPath,
            source,
            module.BehaviorId,
            graph,
            emittedManifest);
        File.WriteAllText(
            debugMapPath,
            JsonSerializer.Serialize(debugMap, ManifestJsonOptions),
            Encoding.UTF8);

        return new DebugScriptEmitResult(
            assemblyPath,
            pdbPath,
            instrumentedSourcePath,
            probeManifestPath,
            debugMapPath,
            emittedManifest,
            debugMap);
    }

    private static IReadOnlyList<MetadataReference> GetPlatformReferences()
    {
        var referenceAssemblyPaths = GetReferenceAssemblyPaths();
        return referenceAssemblyPaths.Count > 0
            ? referenceAssemblyPaths
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray()
            : GetTrustedPlatformReferences();
    }

    private static IReadOnlyList<string> GetReferenceAssemblyPaths()
    {
        var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent;
        if (dotnetRoot is null)
        {
            return Array.Empty<string>();
        }

        var referencePackRoot = Path.Combine(dotnetRoot.FullName, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(referencePackRoot))
        {
            return Array.Empty<string>();
        }

        var targetFramework = $"net{Environment.Version.Major}.0";
        var referenceDirectory = Directory
            .EnumerateDirectories(referencePackRoot)
            .Select(versionDirectory => Path.Combine(versionDirectory, "ref", targetFramework))
            .Where(Directory.Exists)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return referenceDirectory is null
            ? Array.Empty<string>()
            : Directory
                .EnumerateFiles(referenceDirectory, "*.dll")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static IReadOnlyList<MetadataReference> GetTrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !Path.GetFileName(path).StartsWith("ScriptLab", StringComparison.OrdinalIgnoreCase))
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static SyntaxTree ParseSource(string source, string path)
    {
        return CSharpSyntaxTree.ParseText(
            SourceText.From(source, Encoding.UTF8),
            path: path);
    }

    private static string SanitizeAssemblyName(string behaviorId)
    {
        var builder = new StringBuilder(behaviorId.Length);

        foreach (var character in behaviorId)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' || character == '.'
                ? character
                : '_');
        }

        return builder.ToString();
    }

    private static string GetGlobalUsingsSource()
    {
        return """
               global using System;
               global using System.Collections.Generic;
               global using System.Linq;
               """;
    }

    private static string GetDebugProbeSource()
    {
        return """
               #nullable enable

               namespace Asharia.Behavior;

               public sealed record DebugProbeEvent(long Sequence, string Kind, int ProbeId, string? PinId, object? Value);

               public static class DebugProbe
               {
                   private static readonly List<DebugProbeEvent> events = new();
                   private static readonly HashSet<int> breakpoints = new();
                   private static long nextSequence;
                   private static bool traceEnabled;
                   private static bool watchEnabled;

                   public static IReadOnlyList<DebugProbeEvent> Events => events;

                   public static IReadOnlySet<int> Breakpoints => breakpoints;

                   public static bool TraceEnabled => traceEnabled;

                   public static bool WatchEnabled => watchEnabled;

                   public static void Clear()
                   {
                       events.Clear();
                   }

                   public static void SetBreakpoint(int probeId, bool enabled)
                   {
                       if (enabled)
                       {
                           breakpoints.Add(probeId);
                           return;
                       }

                       breakpoints.Remove(probeId);
                   }

                   public static void ClearBreakpoints()
                   {
                       breakpoints.Clear();
                   }

                   public static void SetTraceEnabled(bool enabled)
                   {
                       traceEnabled = enabled;
                   }

                   public static void SetWatchEnabled(bool enabled)
                   {
                       watchEnabled = enabled;
                   }

                   public static void Enter(int probeId)
                   {
                       if (traceEnabled)
                       {
                           events.Add(new DebugProbeEvent(++nextSequence, "Enter", probeId, null, null));
                       }

                       ReportBreakpoint(probeId);
                   }

                   public static T Value<T>(int probeId, string pinId, T value)
                   {
                       if (watchEnabled)
                       {
                           events.Add(new DebugProbeEvent(++nextSequence, "Value", probeId, pinId, value));
                       }

                       ReportBreakpoint(probeId);
                       return value;
                   }

                   private static void ReportBreakpoint(int probeId)
                   {
                       if (breakpoints.Contains(probeId))
                       {
                           events.Add(new DebugProbeEvent(++nextSequence, "Breakpoint", probeId, null, null));
                       }
                   }
               }
               """;
    }

}

public static class DebugScriptCompiler
{
    public static DebugScriptEmitResult EmitFile(string scriptPath, string outputDirectory)
    {
        return SourceInstrumentedDebugCompiler.EmitFile(scriptPath, outputDirectory);
    }
}
