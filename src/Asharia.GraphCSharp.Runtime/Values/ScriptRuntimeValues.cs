using System.Globalization;

namespace ScriptLab;

public sealed record ScriptRuntimeEntityRef(int EntityId)
{
    public override string ToString()
    {
        return $"Entity({EntityId.ToString(CultureInfo.InvariantCulture)})";
    }
}

public sealed record ScriptRuntimeVec3(float X, float Y, float Z)
{
    public override string ToString()
    {
        return $"Vec3({X.ToString(CultureInfo.InvariantCulture)}, {Y.ToString(CultureInfo.InvariantCulture)}, {Z.ToString(CultureInfo.InvariantCulture)})";
    }
}
