using ScriptLab;

var command = args.Length > 0 && args[0] == "dump-ir" ? "dump-ir" : "parse";
var pathArgumentIndex = command == "dump-ir" ? 1 : 0;
var scriptPath = args.Length > pathArgumentIndex
    ? args[pathArgumentIndex]
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

ConsoleScriptReporter.Write(result, Console.Out);

return result.HasErrors ? 1 : 0;
