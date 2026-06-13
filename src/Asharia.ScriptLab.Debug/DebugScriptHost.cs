using System.Reflection;
using System.Runtime.Loader;

namespace ScriptLab;

public sealed class DebugScriptHost : DotnetDebugHost
{
    private DebugScriptHost(
        AssemblyLoadContext loadContext,
        Assembly assembly,
        DebugProbeManifest? probeManifest)
        : base(loadContext, assembly, probeManifest)
    {
    }

    public static new DebugScriptHost Load(DebugScriptEmitResult emit)
    {
        return Load(emit.AssemblyPath, emit.PdbPath, emit.ProbeManifest);
    }

    public static new DebugScriptHost Load(string assemblyPath, string? pdbPath = null)
    {
        return Load(assemblyPath, pdbPath, probeManifest: null);
    }

    public static new DebugScriptHost Load(
        string assemblyPath,
        string? pdbPath,
        DebugProbeManifest? probeManifest)
    {
        return LoadCore(
            assemblyPath,
            pdbPath,
            probeManifest,
            (loadContext, assembly, manifest) => new DebugScriptHost(loadContext, assembly, manifest));
    }
}
