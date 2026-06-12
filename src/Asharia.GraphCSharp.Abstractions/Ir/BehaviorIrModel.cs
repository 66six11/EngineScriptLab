namespace ScriptLab;

public sealed record BehaviorIrModule(
    string BehaviorId,
    IReadOnlyList<BehaviorIrField> Fields,
    IReadOnlyList<BehaviorIrFunction> Functions);

public sealed record BehaviorIrField(FieldId FieldId, string Name, string Type, string? InitialValue);

public sealed record BehaviorIrFunction(
    string Name,
    IReadOnlyList<BehaviorIrParameter> Parameters,
    IReadOnlyList<BehaviorIrBlock> Blocks);

public sealed record BehaviorIrParameter(string Name, string Type);

public sealed record BehaviorIrBlock(string Name, IReadOnlyList<BehaviorIrInstruction> Instructions);

public sealed record BehaviorSourceSpan(string FileName, int Line, int Column, int Start, int Length)
{
    public static BehaviorSourceSpan Generated { get; } = new("<generated>", 0, 0, 0, 0);
}

public static class BehaviorIrBreakabilityHint
{
    public const string Breakable = "breakable";
    public const string Observable = "observable";
    public const string SourceOnly = "sourceOnly";
}

public abstract record BehaviorIrInstruction(BehaviorSourceSpan Source)
{
    public string DebugSiteId { get; init; } = string.Empty;

    public string BreakabilityHint { get; init; } = BehaviorIrBreakabilityHint.Observable;

    public bool Observable { get; init; } = true;
}

public abstract record BehaviorIrValueInstruction(string Target, BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrLoadConst(string Target, string Value, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadEnum(string Target, string Value, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadField(string Target, FieldId FieldId, string FieldName, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadLocal(string Target, string LocalName, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadSelf(string Target, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadMember(string Target, string Member, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrBinaryOp(
    string Target,
    string Operator,
    string Left,
    string Right,
    BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrMakeStruct(
    string Target,
    string Type,
    IReadOnlyList<string> Arguments,
    BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrCallFunction(
    string? Target,
    FunctionId FunctionId,
    IReadOnlyList<string> Arguments,
    BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrBranch(
    string Condition,
    string ThenBlock,
    string ElseBlock,
    BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrJump(string TargetBlock, BehaviorSourceSpan Source) : BehaviorIrInstruction(Source);

public sealed record BehaviorIrReturn(BehaviorSourceSpan Source) : BehaviorIrInstruction(Source);

public sealed record BehaviorIrDeclareLocal(string LocalName, BehaviorSourceSpan Source) : BehaviorIrInstruction(Source);

public sealed record BehaviorIrStoreLocal(string LocalName, string Value, BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrAssign(string TargetExpression, string Value, BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrDebugWatch(
    string Name,
    string Value,
    bool IsStatement,
    BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);
