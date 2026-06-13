namespace ScriptLab;

public sealed class ScriptDebugSession : DebugSessionCore
{
    public ScriptDebugSession(ScriptDebugMap debugMap, string sourceText, int traceSampleCapacity = 256)
        : base(debugMap, sourceText, traceSampleCapacity)
    {
    }

    public ScriptDebugSession(
        ScriptDebugMap debugMap,
        string sourceText,
        string sourcePath,
        int traceSampleCapacity = 256)
        : base(debugMap, sourceText, sourcePath, traceSampleCapacity)
    {
    }
}
