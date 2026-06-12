namespace ScriptLab;

public sealed record RuntimeDiagnostic(
    string Code,
    string Message,
    string? DebugSiteId,
    BehaviorSourceSpan Source)
{
    public const string InvalidEntity = "SLRT0001";
    public const string UnsupportedInstruction = "SLRT0002";
    public const string MissingValue = "SLRT0003";
    public const string MissingBlock = "SLRT0004";
    public const string MissingFunction = "SLRT0005";
    public const string StepLimitExceeded = "SLRT0006";
    public const string UnsupportedFunction = "SLRT0007";
    public const string InvalidRuntimeValue = "SLRT0008";
}
