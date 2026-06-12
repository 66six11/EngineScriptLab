namespace ScriptLab;

public sealed class BehaviorProgram
{
    private readonly IReadOnlyDictionary<string, BehaviorIrFunction> functionsByName;
    private readonly IReadOnlyDictionary<FieldId, BehaviorIrField> fieldsById;

    private BehaviorProgram(BehaviorIrModule module)
    {
        Module = module;
        functionsByName = module.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        fieldsById = module.Fields.ToDictionary(field => field.FieldId, EqualityComparer<FieldId>.Default);
    }

    public BehaviorIrModule Module { get; }

    public string BehaviorId => Module.BehaviorId;

    public IReadOnlyList<BehaviorIrField> Fields => Module.Fields;

    public static BehaviorProgram FromModule(BehaviorIrModule module)
    {
        return new BehaviorProgram(module);
    }

    public bool TryGetFunction(string name, out BehaviorIrFunction function)
    {
        var found = functionsByName.TryGetValue(name, out var candidate);
        function = candidate!;
        return found;
    }

    public bool TryGetField(FieldId fieldId, out BehaviorIrField field)
    {
        var found = fieldsById.TryGetValue(fieldId, out var candidate);
        field = candidate!;
        return found;
    }
}
