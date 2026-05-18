using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
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

public static class DebugScriptCompiler
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
            GetTrustedPlatformReferences(),
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

                   public static IReadOnlyList<DebugProbeEvent> Events => events;

                   public static IReadOnlySet<int> Breakpoints => breakpoints;

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

                   public static void Enter(int probeId)
                   {
                       events.Add(new DebugProbeEvent(++nextSequence, "Enter", probeId, null, null));
                       ReportBreakpoint(probeId);
                   }

                   public static T Value<T>(int probeId, string pinId, T value)
                   {
                       events.Add(new DebugProbeEvent(++nextSequence, "Value", probeId, pinId, value));
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

    private static class DebugProbeManifestFactory
    {
        public static DebugProbeManifest Create(
            string behaviorId,
            string sourcePath,
            BlueprintGraphModule graph)
        {
            var usedProbeIds = new HashSet<int>();
            var probes = new List<DebugProbeSite>();

            foreach (var function in graph.Functions)
            {
                foreach (var node in function.Nodes)
                {
                    if (node.Kind is not ("Branch" or "Call" or "Watch"))
                    {
                        continue;
                    }

                    probes.Add(CreateSite(behaviorId, function.Name, node, usedProbeIds));
                }
            }

            return new DebugProbeManifest(behaviorId, sourcePath, probes);
        }

        private static DebugProbeSite CreateSite(
            string behaviorId,
            string functionName,
            BlueprintGraphNode node,
            ISet<int> usedProbeIds)
        {
            var baseKey = string.Join(
                "|",
                behaviorId,
                functionName,
                node.DebugSiteId,
                node.Kind,
                node.Label,
                node.Source.FileName,
                node.Source.Start,
                node.Source.Length);
            var probeId = CreateUniqueProbeId(baseKey, usedProbeIds);

            return new DebugProbeSite(
                probeId,
                node.DebugSiteId,
                node.Id,
                node.Kind,
                node.Label,
                node.Source,
                node.BreakabilityHint,
                node.BreakableVerified,
                CreatePins(node));
        }

        private static int CreateUniqueProbeId(string key, ISet<int> usedProbeIds)
        {
            var suffix = 0;
            while (true)
            {
                var candidate = StablePositiveHash(suffix == 0 ? key : $"{key}|{suffix}");
                if (usedProbeIds.Add(candidate))
                {
                    return candidate;
                }

                suffix++;
            }
        }

        private static int StablePositiveHash(string text)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            var hash = offsetBasis;

            foreach (var character in text)
            {
                hash ^= character;
                hash *= prime;
            }

            return (int)(hash & 0x7fffffff);
        }

        private static IReadOnlyList<DebugProbePin> CreatePins(BlueprintGraphNode node)
        {
            return node.Kind == "Watch"
                ? new[] { new DebugProbePin(node.Label, "value") }
                : Array.Empty<DebugProbePin>();
        }
    }

    private static class ScriptDebugMapFactory
    {
        public static ScriptDebugMap Create(
            string assemblyPath,
            string pdbPath,
            string sourcePath,
            string source,
            string behaviorId,
            BlueprintGraphModule graph,
            DebugProbeManifest probeManifest)
        {
            var probeByDebugSiteId = probeManifest.Probes.ToDictionary(
                probe => probe.DebugSiteId,
                StringComparer.Ordinal);
            var sourceChecksum = ComputeSha256Hex(source);
            var sourceDocumentPath = Path.GetFullPath(sourcePath);
            var assemblyMvid = ReadAssemblyMvid(assemblyPath);
            var pdbInfo = ReadPortablePdb(pdbPath);
            var functions = graph.Functions
                .Select(function => new ScriptDebugMapFunction(
                    function.Name,
                    function.Nodes
                        .Select(node =>
                        {
                            probeByDebugSiteId.TryGetValue(node.DebugSiteId, out var probe);
                            var sequencePoint = FindSequencePoint(pdbInfo.SequencePoints, sourceDocumentPath, node.Source);
                            var localScope = sequencePoint is null
                                ? null
                                : FindLocalScope(pdbInfo.LocalScopes, sequencePoint.MethodToken, sequencePoint.Offset);
                            var breakableVerified = node.BreakabilityHint == BehaviorIrBreakabilityHint.Breakable &&
                                sequencePoint is not null;
                            return new ScriptDebugMapSite(
                                node.DebugSiteId,
                                node.DebugSiteId,
                                node.Source,
                                ComputeSourceTextHash(source, node.Source),
                                node.Id,
                                node.Kind,
                                node.Label,
                                node.BreakabilityHint,
                                breakableVerified,
                                node.Observable,
                                node.OwningBreakableDebugSiteId,
                                probe?.ProbeId,
                                sequencePoint?.MethodToken,
                                sequencePoint?.Offset,
                                sequencePoint?.EndOffset,
                                sequencePoint is null
                                    ? null
                                    : new ScriptDebugMapSequencePoint(
                                        sequencePoint.DocumentPath,
                                        sequencePoint.StartLine,
                                        sequencePoint.StartColumn,
                                        sequencePoint.EndLine,
                                        sequencePoint.EndColumn),
                                localScope is null
                                    ? null
                                    : new ScriptDebugMapLocalScope(
                                        localScope.MethodToken,
                                        localScope.StartOffset,
                                        localScope.Length,
                                        localScope.LocalNames));
                        })
                        .ToArray()))
                .ToArray();

            return new ScriptDebugMap(
                SchemaVersion: 1,
                BuildId: Guid.NewGuid().ToString("N"),
                AssemblyMvid: assemblyMvid,
                PdbId: pdbInfo.PdbId,
                SourceDocumentPath: sourceDocumentPath,
                SourceChecksum: sourceChecksum,
                BehaviorId: behaviorId,
                Functions: functions);
        }

        private static string ReadAssemblyMvid(string assemblyPath)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();
            var module = metadataReader.GetModuleDefinition();
            return metadataReader.GetGuid(module.Mvid).ToString("D");
        }

        private static (string PdbId, IReadOnlyList<PortablePdbSequencePointInfo> SequencePoints, IReadOnlyList<PortablePdbLocalScopeInfo> LocalScopes) ReadPortablePdb(string pdbPath)
        {
            using var stream = File.OpenRead(pdbPath);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var metadataReader = provider.GetMetadataReader();
            var header = metadataReader.DebugMetadataHeader
                ?? throw new InvalidOperationException($"Portable PDB '{pdbPath}' does not contain a debug metadata header.");
            var sequencePoints = ReadSequencePoints(metadataReader);
            var localScopes = ReadLocalScopes(metadataReader);
            return (ToHex(header.Id), sequencePoints, localScopes);
        }

        private static IReadOnlyList<PortablePdbSequencePointInfo> ReadSequencePoints(MetadataReader metadataReader)
        {
            var results = new List<PortablePdbSequencePointInfo>();

            foreach (var handle in metadataReader.MethodDebugInformation)
            {
                var debugInformation = metadataReader.GetMethodDebugInformation(handle);
                var methodToken = FormatMetadataToken(MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber(handle))));
                var methodSequencePoints = debugInformation
                    .GetSequencePoints()
                    .Where(sequencePoint => !sequencePoint.IsHidden)
                    .ToArray();

                for (var index = 0; index < methodSequencePoints.Length; index++)
                {
                    var sequencePoint = methodSequencePoints[index];
                    var documentHandle = sequencePoint.Document.IsNil
                        ? debugInformation.Document
                        : sequencePoint.Document;
                    var document = metadataReader.GetDocument(documentHandle);
                    var documentPath = metadataReader.GetString(document.Name);
                    var endOffset = index + 1 < methodSequencePoints.Length
                        ? methodSequencePoints[index + 1].Offset
                        : (int?)null;

                    results.Add(new PortablePdbSequencePointInfo(
                        methodToken,
                        sequencePoint.Offset,
                        endOffset,
                        documentPath,
                        sequencePoint.StartLine,
                        sequencePoint.StartColumn,
                        sequencePoint.EndLine,
                        sequencePoint.EndColumn));
                }
            }

            return results;
        }

        private static IReadOnlyList<PortablePdbLocalScopeInfo> ReadLocalScopes(MetadataReader metadataReader)
        {
            var results = new List<PortablePdbLocalScopeInfo>();

            foreach (var handle in metadataReader.LocalScopes)
            {
                var localScope = metadataReader.GetLocalScope(handle);
                var localNames = localScope
                    .GetLocalVariables()
                    .Select(variableHandle => metadataReader.GetString(metadataReader.GetLocalVariable(variableHandle).Name))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray();
                results.Add(new PortablePdbLocalScopeInfo(
                    FormatMetadataToken(MetadataTokens.GetToken(localScope.Method)),
                    localScope.StartOffset,
                    localScope.Length,
                    localNames));
            }

            return results;
        }

        private static PortablePdbSequencePointInfo? FindSequencePoint(
            IEnumerable<PortablePdbSequencePointInfo> sequencePoints,
            string sourceDocumentPath,
            BehaviorSourceSpan sourceSpan)
        {
            if (sourceSpan.Line <= 0 || sourceSpan.Column <= 0)
            {
                return null;
            }

            return sequencePoints
                .Where(sequencePoint =>
                    PathsEqual(sequencePoint.DocumentPath, sourceDocumentPath) &&
                    sequencePoint.StartLine == sourceSpan.Line &&
                    sequencePoint.StartColumn == sourceSpan.Column)
                .OrderBy(sequencePoint => sequencePoint.Offset)
                .FirstOrDefault();
        }

        private static PortablePdbLocalScopeInfo? FindLocalScope(
            IEnumerable<PortablePdbLocalScopeInfo> localScopes,
            string methodToken,
            int ilOffset)
        {
            return localScopes
                .Where(localScope =>
                    string.Equals(localScope.MethodToken, methodToken, StringComparison.Ordinal) &&
                    localScope.StartOffset <= ilOffset &&
                    ilOffset < localScope.StartOffset + localScope.Length)
                .OrderBy(localScope => localScope.Length)
                .FirstOrDefault();
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

        private static string FormatMetadataToken(int token)
        {
            return $"0x{token:x8}";
        }

        private static string ComputeSourceTextHash(string source, BehaviorSourceSpan sourceSpan)
        {
            if (sourceSpan.Start < 0 ||
                sourceSpan.Length <= 0 ||
                sourceSpan.Start + sourceSpan.Length > source.Length)
            {
                return string.Empty;
            }

            return ComputeSha256Hex(source.Substring(sourceSpan.Start, sourceSpan.Length));
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
    }

    private sealed class DebugInstrumentationRewriter : CSharpSyntaxRewriter
    {
        private readonly DebugProbeManifest manifest;
        private readonly List<DebugProbeSite> probeSites = new();

        public DebugInstrumentationRewriter(DebugProbeManifest manifest)
        {
            this.manifest = manifest;
        }

        public IReadOnlyList<DebugProbeSite> ProbeSites => probeSites;

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            var rewrittenStatements = new List<StatementSyntax>();

            foreach (var statement in node.Statements)
            {
                DebugProbeSite? site = null;
                if (TryCreateEnterProbe(statement, out var probe, out site))
                {
                    rewrittenStatements.Add(probe);
                }

                var rewrittenStatement = (StatementSyntax)Visit(statement)!;
                if (site is not null)
                {
                    rewrittenStatement = AddSourceLineDirective(rewrittenStatement, statement, site);
                }

                rewrittenStatements.Add(rewrittenStatement);
            }

            return node.WithStatements(SyntaxFactory.List(rewrittenStatements));
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (TryRewriteGraphDebugInvocation(node, out var rewritten))
            {
                return rewritten;
            }

            return base.VisitInvocationExpression(node);
        }

        private bool TryRewriteGraphDebugInvocation(
            InvocationExpressionSyntax invocation,
            out ExpressionSyntax rewritten)
        {
            rewritten = null!;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Expression.ToString() != "GraphDebug" ||
                memberAccess.Name.Identifier.ValueText is not ("Inspect" or "Watch"))
            {
                return false;
            }

            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count < 2)
            {
                return false;
            }

            var watchProbe = FindProbe("Watch", invocation.Span);
            if (watchProbe is null)
            {
                return false;
            }

            var pinId = GetDebugWatchName(arguments[0].Expression);
            var value = (ExpressionSyntax)Visit(arguments[1].Expression)!;
            AddProbeSite(watchProbe);
            rewritten = SyntaxFactory.ParseExpression(
                    $"global::Asharia.Behavior.DebugProbe.Value({watchProbe.ProbeId}, \"{EscapeString(pinId)}\", {value})")
                .WithTriviaFrom(invocation);
            return true;
        }

        private bool TryCreateEnterProbe(
            StatementSyntax statement,
            out StatementSyntax probe,
            out DebugProbeSite? site)
        {
            site = FindEnterProbe(statement);
            if (site is null)
            {
                probe = null!;
                return false;
            }

            AddProbeSite(site);
            probe = SyntaxFactory
                .ParseStatement($"global::Asharia.Behavior.DebugProbe.Enter({site.ProbeId});")
                .WithLeadingTrivia(statement.GetLeadingTrivia()
                    .AddRange(SyntaxFactory.ParseLeadingTrivia($"#line hidden{Environment.NewLine}"))
                    .AddRange(GetIndentationTrivia(statement.GetLeadingTrivia())))
                .WithTrailingTrivia(SyntaxFactory.ParseTrailingTrivia(
                    Environment.NewLine));
            return true;
        }

        private StatementSyntax AddSourceLineDirective(
            StatementSyntax rewrittenStatement,
            StatementSyntax originalStatement,
            DebugProbeSite site)
        {
            var sourcePath = EscapeLineDirectivePath(manifest.SourcePath);
            var originalLeadingTrivia = originalStatement.GetLeadingTrivia();
            return rewrittenStatement.WithLeadingTrivia(originalLeadingTrivia
                .AddRange(SyntaxFactory.ParseLeadingTrivia($"#line {site.Source.Line} \"{sourcePath}\"{Environment.NewLine}"))
                .AddRange(GetIndentationTrivia(originalLeadingTrivia)));
        }

        private DebugProbeSite? FindEnterProbe(StatementSyntax statement)
        {
            return statement switch
            {
                IfStatementSyntax ifStatement => FindProbe("Branch", ifStatement.Span),
                ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } =>
                    FindProbe("Call", invocation.Span),
                _ => null
            };
        }

        private DebugProbeSite? FindProbe(string kind, TextSpan span)
        {
            foreach (var probe in manifest.Probes)
            {
                if (probe.Kind == kind &&
                    probe.Source.Start == span.Start &&
                    probe.Source.Length == span.Length)
                {
                    return probe;
                }
            }

            return null;
        }

        private void AddProbeSite(DebugProbeSite site)
        {
            if (probeSites.All(existing => existing.ProbeId != site.ProbeId))
            {
                probeSites.Add(site);
            }
        }

        private static string GetDebugWatchName(ExpressionSyntax expression)
        {
            return expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression)
                    ? literal.Token.ValueText
                    : expression.ToString();
        }

        private static string EscapeString(string text)
        {
            return text
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }

        private static SyntaxTriviaList GetIndentationTrivia(SyntaxTriviaList trivia)
        {
            var indentation = trivia
                .Reverse()
                .TakeWhile(item => item.IsKind(SyntaxKind.WhitespaceTrivia))
                .Reverse();
            return SyntaxFactory.TriviaList(indentation);
        }

        private static string EscapeLineDirectivePath(string path)
        {
            return Path.GetFullPath(path)
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
        }
    }
}
