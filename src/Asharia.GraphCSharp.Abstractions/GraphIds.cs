using System.Globalization;

namespace ScriptLab;

public readonly record struct TypeId(string Value)
{
    public override string ToString()
    {
        return Value;
    }
}

public readonly record struct FieldId(int Value)
{
    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}

public readonly record struct FunctionId(string Value)
{
    public override string ToString()
    {
        return Value;
    }
}
