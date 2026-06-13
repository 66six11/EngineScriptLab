namespace ScriptLab;

public static class DebugScriptCompiler
{
    public static DebugScriptEmitResult EmitFile(string scriptPath, string outputDirectory)
    {
        return SourceInstrumentedDebugCompiler.EmitFile(scriptPath, outputDirectory);
    }
}
