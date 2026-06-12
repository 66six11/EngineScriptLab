namespace ScriptLab;

public sealed class ScriptInstance
{
    private readonly Dictionary<FieldId, object?> fields;

    private ScriptInstance(BehaviorProgram program, int entityId, Dictionary<FieldId, object?> fields)
    {
        Program = program;
        EntityId = entityId;
        this.fields = fields;
    }

    public BehaviorProgram Program { get; }

    public int EntityId { get; }

    public IReadOnlyDictionary<FieldId, object?> Fields => fields;

    public static ScriptInstance Create(BehaviorProgram program, int entityId)
    {
        var fields = program.Fields.ToDictionary(
            field => field.FieldId,
            field => field.InitialValue is null
                ? ScriptRuntimeValueParser.GetDefaultValue(field.Type)
                : ScriptRuntimeValueParser.ParseLiteral(field.InitialValue),
            EqualityComparer<FieldId>.Default);

        return new ScriptInstance(program, entityId, fields);
    }

    internal bool TryGetField(FieldId fieldId, out object? value)
    {
        return fields.TryGetValue(fieldId, out value);
    }
}
