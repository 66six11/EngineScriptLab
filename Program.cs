using ScriptLab;
using System.Text.Json;

var command = args.Length > 0 && (args[0] == "dump-ir" ||
                                  args[0] == "dump-graph" ||
                                  args[0] == "emit-debug" ||
                                  args[0] == "inspect-debug" ||
                                  args[0] == "break-debug" ||
                                  args[0] == "run-debug" ||
                                  args[0] == "debug-smoke" ||
                                  args[0] == "server" ||
                                  args[0] == "verify-ir" ||
                                  args[0] == "run-ir")
    ? args[0]
    : "parse";
if (command == "run-ir")
{
    command = "verify-ir";
}
var pathArgumentIndex = command == "parse" ? 0 : 1;
var commandArgs = args
    .Skip(pathArgumentIndex)
    .ToArray();
var optionArgs = commandArgs
    .Where(argument => argument.StartsWith("--", StringComparison.Ordinal))
    .ToHashSet(StringComparer.Ordinal);
var positionalArgs = commandArgs
    .Where(argument => !argument.StartsWith("--", StringComparison.Ordinal))
    .ToArray();

if (command == "server")
{
    var dapAdapterPath = GetOptionValue(commandArgs, "--dap-adapter") ??
                         Environment.GetEnvironmentVariable(ScriptLabJsonRpcServer.DapAdapterPathEnvironmentVariable);
    using var server = new ScriptLabJsonRpcServer(new ScriptLabServerOptions(DapAdapterPath: dapAdapterPath));
    await server.RunAsync(Console.In, Console.Out);
    return 0;
}

var scriptPath = positionalArgs.Length > 0
    ? positionalArgs[0]
    : Path.Combine("Samples", "PlayerMove.ash.cs");
var fullPath = ScriptPathResolver.Resolve(scriptPath);

if (!File.Exists(fullPath))
{
    Console.Error.WriteLine($"Script file not found: {fullPath}");
    return 1;
}

var result = GraphCSharpScriptParser.ParseFile(fullPath);

if (command == "dump-ir")
{
    if (result.HasErrors)
    {
        ConsoleScriptReporter.Write(result, Console.Out);
        return 1;
    }

    var module = BehaviorIrLowerer.LowerFile(fullPath);
    BehaviorIrReporter.Write(module, Console.Out);
    return 0;
}

if (command == "dump-graph")
{
    if (result.HasErrors)
    {
        ConsoleScriptReporter.Write(result, Console.Out);
        return 1;
    }

    var module = BehaviorIrLowerer.LowerFile(fullPath);
    var graph = BlueprintGraphProjector.Project(module);
    if (optionArgs.Contains("--json"))
    {
        BlueprintGraphReporter.WriteJson(graph, Console.Out);
    }
    else
    {
        BlueprintGraphReporter.Write(graph, Console.Out);
    }

    return 0;
}

if (command == "verify-ir")
{
    if (result.HasErrors)
    {
        ConsoleScriptReporter.Write(result, Console.Out);
        return 1;
    }

    var module = BehaviorIrLowerer.LowerFile(fullPath);
    var verification = BehaviorIrVerifier.ExecuteUpdate(
        module,
        delta: 0.016f,
        downKeys: new HashSet<string>(StringComparer.Ordinal) { "Key.W" });
    BehaviorIrVerificationReporter.Write(verification, Console.Out);
    return 0;
}

if (command == "emit-debug")
{
    if (result.HasErrors)
    {
        ConsoleScriptReporter.Write(result, Console.Out);
        return 1;
    }

    var outputDirectory = positionalArgs.Length > 1
        ? Path.GetFullPath(positionalArgs[1])
        : Path.GetFullPath(Path.Combine("bin", "ScriptDebug"));
    var emit = DebugScriptCompiler.EmitFile(fullPath, outputDirectory);

    Console.Out.WriteLine("DebugEmit:");
    Console.Out.WriteLine($"  Assembly: {emit.AssemblyPath}");
    Console.Out.WriteLine($"  Pdb: {emit.PdbPath}");
    Console.Out.WriteLine($"  InstrumentedSource: {emit.InstrumentedSourcePath}");
    Console.Out.WriteLine($"  ProbeManifest: {emit.ProbeManifestPath}");
    Console.Out.WriteLine($"  DebugMap: {emit.DebugMapPath}");
    Console.Out.WriteLine($"  BuildId: {emit.DebugMap.BuildId}");
    Console.Out.WriteLine($"  AssemblyMvid: {emit.DebugMap.AssemblyMvid}");
    Console.Out.WriteLine($"  PdbId: {emit.DebugMap.PdbId}");
    Console.Out.WriteLine("  ProbeSites:");

    foreach (var site in emit.ProbeSites)
    {
        Console.Out.WriteLine(
            $"    {site.ProbeId} {site.DebugSiteId} {site.GraphNodeId} {site.Kind} {site.Label} @ {BehaviorIrText.FormatSource(site.Source)}");
    }

    return 0;
}

if (command == "inspect-debug")
{
    if (result.HasErrors)
    {
        ConsoleScriptReporter.Write(result, Console.Out);
        return 1;
    }

    var outputDirectory = positionalArgs.Length > 1
        ? Path.GetFullPath(positionalArgs[1])
        : Path.GetFullPath(Path.Combine("bin", "ScriptDebug"));
    var emit = DebugScriptCompiler.EmitFile(fullPath, outputDirectory);
    var host = DebugScriptHost.Load(emit);
    var instance = host.MountBehavior(entityId: 1, emit.ProbeManifest.BehaviorId);

    DebugScriptHostReporter.Write(instance, Console.Out);
    return 0;
}

if (command == "break-debug")
{
    if (result.HasErrors)
    {
        ConsoleScriptReporter.Write(result, Console.Out);
        return 1;
    }

    var outputDirectory = positionalArgs.Length > 1
        ? Path.GetFullPath(positionalArgs[1])
        : Path.GetFullPath(Path.Combine("bin", "ScriptDebug"));
    var emit = DebugScriptCompiler.EmitFile(fullPath, outputDirectory);
    var host = DebugScriptHost.Load(emit);
    var graphNodeId = positionalArgs.Length > 2
        ? positionalArgs[2]
        : null;
    var site = graphNodeId is null
        ? emit.ProbeSites.FirstOrDefault()
        : host.ResolveProbeSiteByGraphNodeId(graphNodeId);

    if (site is null)
    {
        Console.Out.WriteLine("BreakpointDebug:");
        Console.Out.WriteLine("  ProbeSites: <none>");
        return 0;
    }

    var instance = host.MountBehavior(entityId: 1, emit.ProbeManifest.BehaviorId);
    host.SetBreakpoint(site.ProbeId, enabled: true);
    host.ClearProbeEvents();
    instance.InvokeUpdate(0.016f);

    Console.Out.WriteLine("BreakpointDebug:");
    Console.Out.WriteLine($"  Entity: {instance.EntityId}");
    Console.Out.WriteLine($"  BehaviorId: {instance.BehaviorId}");
    Console.Out.WriteLine($"  Breakpoint: {site.ProbeId} {site.DebugSiteId} {site.GraphNodeId} {site.Kind} {site.Label}");
    Console.Out.WriteLine("  Events:");

    foreach (var probeEvent in host.GetProbeEvents())
    {
        var pin = probeEvent.PinId is null ? string.Empty : $" pin={probeEvent.PinId}";
        var value = probeEvent.Value is null ? string.Empty : $" value={probeEvent.Value}";
        Console.Out.WriteLine($"    #{probeEvent.Sequence} {probeEvent.Kind} probe={probeEvent.ProbeId}{pin}{value}");
    }

    return 0;
}

if (command == "run-debug")
{
    if (result.HasErrors)
    {
        ConsoleScriptReporter.Write(result, Console.Out);
        return 1;
    }

    var outputDirectory = positionalArgs.Length > 1
        ? Path.GetFullPath(positionalArgs[1])
        : Path.GetFullPath(Path.Combine("bin", "ScriptDebug"));
    var graphNodeId = positionalArgs.Length > 2
        ? positionalArgs[2]
        : null;
    var loop = ScriptDebugLoop.RunFile(
        fullPath,
        outputDirectory,
        new ScriptDebugLoopOptions(GraphNodeId: graphNodeId));
    var jsonOutputPath = GetOptionValue(commandArgs, "--json-out");
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    if (optionArgs.Contains("--json") || jsonOutputPath is not null)
    {
        var json = JsonSerializer.Serialize(loop, jsonOptions);
        if (jsonOutputPath is not null)
        {
            var fullJsonOutputPath = Path.GetFullPath(jsonOutputPath);
            var jsonOutputDirectory = Path.GetDirectoryName(fullJsonOutputPath);
            if (!string.IsNullOrEmpty(jsonOutputDirectory))
            {
                Directory.CreateDirectory(jsonOutputDirectory);
            }

            File.WriteAllText(fullJsonOutputPath, json);
            if (!optionArgs.Contains("--json"))
            {
                Console.Out.WriteLine($"DebugLoopJson: {fullJsonOutputPath}");
            }
        }

        if (optionArgs.Contains("--json"))
        {
            Console.Out.WriteLine(json);
        }
    }
    else
    {
        ScriptDebugLoopReporter.Write(loop, Console.Out);
    }

    return 0;
}

if (command == "debug-smoke")
{
    if (result.HasErrors)
    {
        ConsoleScriptReporter.Write(result, Console.Out);
        return 1;
    }

    var outputDirectory = positionalArgs.Length > 1
        ? Path.GetFullPath(positionalArgs[1])
        : Path.GetFullPath(Path.Combine("bin", "ScriptDebug"));
    var graphNodeId = positionalArgs.Length > 2
        ? positionalArgs[2]
        : null;
    var smoke = ScriptDebugSmoke.RunFile(
        fullPath,
        outputDirectory,
        new ScriptDebugSmokeOptions(GraphNodeId: graphNodeId));
    var jsonOutputPath = GetOptionValue(commandArgs, "--json-out");
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    if (optionArgs.Contains("--json") || jsonOutputPath is not null)
    {
        var json = JsonSerializer.Serialize(smoke, jsonOptions);
        if (jsonOutputPath is not null)
        {
            var fullJsonOutputPath = Path.GetFullPath(jsonOutputPath);
            var jsonOutputDirectory = Path.GetDirectoryName(fullJsonOutputPath);
            if (!string.IsNullOrEmpty(jsonOutputDirectory))
            {
                Directory.CreateDirectory(jsonOutputDirectory);
            }

            File.WriteAllText(fullJsonOutputPath, json);
            if (!optionArgs.Contains("--json"))
            {
                Console.Out.WriteLine($"DebugSmokeJson: {fullJsonOutputPath}");
            }
        }

        if (optionArgs.Contains("--json"))
        {
            Console.Out.WriteLine(json);
        }
    }
    else
    {
        ScriptDebugSmokeReporter.Write(smoke, Console.Out);
    }

    return smoke.Success ? 0 : 1;
}

ConsoleScriptReporter.Write(result, Console.Out);

return result.HasErrors ? 1 : 0;

static string? GetOptionValue(IEnumerable<string> arguments, string name)
{
    var prefix = name + "=";
    foreach (var argument in arguments)
    {
        if (argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            return argument[prefix.Length..];
        }
    }

    return null;
}
