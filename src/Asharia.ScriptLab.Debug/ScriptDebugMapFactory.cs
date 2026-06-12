using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using ScriptLab.GraphCSharp;

namespace ScriptLab;

internal static class ScriptDebugMapFactory
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
                        var sourceTextHash = ComputeSourceTextHash(source, node.Source);
                        VerifySourceSpan(node, sourceTextHash);
                        var sequencePoint = FindSequencePoint(pdbInfo.SequencePoints, sourceDocumentPath, node.Source);
                        VerifyPdbSequencePoint(node, probe, sequencePoint);
                        var localScope = sequencePoint is null
                            ? null
                            : FindLocalScope(pdbInfo.LocalScopes, sequencePoint.MethodToken, sequencePoint.Offset);
                        var breakableVerified = node.BreakabilityHint == BehaviorIrBreakabilityHint.Breakable &&
                            sequencePoint is not null;
                        return new ScriptDebugMapSite(
                            node.DebugSiteId,
                            node.DebugSiteId,
                            node.Source,
                            sourceTextHash,
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

    private static void VerifySourceSpan(BlueprintGraphNode node, string sourceTextHash)
    {
        if (!RequiresSourceMap(node) || !string.IsNullOrEmpty(sourceTextHash))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{GraphCSharpRuleSet.SourceMapUnavailableId}: " +
            GraphCSharpRuleSet.GetSourceMapUnavailableMessage(
                $"graph node '{node.Kind}:{node.Label}' has no mappable source span."));
    }

    private static void VerifyPdbSequencePoint(
        BlueprintGraphNode node,
        DebugProbeSite? probe,
        PortablePdbSequencePointInfo? sequencePoint)
    {
        if (probe is null ||
            node.BreakabilityHint != BehaviorIrBreakabilityHint.Breakable ||
            sequencePoint is not null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{GraphCSharpRuleSet.SourceMapUnavailableId}: " +
            GraphCSharpRuleSet.GetSourceMapUnavailableMessage(
                $"probe node '{node.Kind}:{node.Label}' has no PDB sequence point."));
    }

    private static bool RequiresSourceMap(BlueprintGraphNode node)
    {
        return node.Observable ||
            node.BreakabilityHint == BehaviorIrBreakabilityHint.Breakable ||
            node.Kind is "Branch" or "Call" or "Watch";
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
