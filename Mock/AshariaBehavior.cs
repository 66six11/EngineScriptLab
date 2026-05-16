namespace Asharia.Behavior;

[AttributeUsage(AttributeTargets.Class)]
public sealed class BehaviorAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class FormerlyBehaviorAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class FieldAttribute : Attribute
{
    public FieldAttribute()
    {
    }

    public FieldAttribute(int id)
    {
        Id = id;
    }

    public int? Id { get; }
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class ExposeAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeFieldAttribute : Attribute;

[AttributeUsage(AttributeTargets.Field)]
public sealed class RangeAttribute(float min, float max) : Attribute
{
    public float Min { get; } = min;
    public float Max { get; } = max;
}

public abstract class BehaviorComponent
{
    protected EntityRef Self => new(0);

    protected virtual void Start()
    {
    }

    protected virtual void Update(float delta)
    {
    }

    protected virtual void FixedUpdate(float delta)
    {
    }

    protected virtual void Destroy()
    {
    }
}

public readonly record struct EntityRef(int Id);

public readonly record struct Vec3(float X, float Y, float Z);

public enum Key
{
    W,
    A,
    S,
    D
}

public static class Input
{
    public static bool KeyDown(Key key) => false;
}

public static class Transform
{
    public static void Translate(EntityRef entity, Vec3 offset)
    {
    }
}
