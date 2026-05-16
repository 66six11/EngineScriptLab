using ScriptLab;

var scriptPath = args.Length > 0 ? args[0] : Path.Combine("Samples", "PlayerMove.ash.cs");
var fullPath = ScriptPathResolver.Resolve(scriptPath);

if (!File.Exists(fullPath))
{
    Console.Error.WriteLine($"Script file not found: {fullPath}");
    return 1;
}

var result = GraphCSharpScriptParser.ParseFile(fullPath);
ConsoleScriptReporter.Write(result, Console.Out);

return result.HasErrors ? 1 : 0;
