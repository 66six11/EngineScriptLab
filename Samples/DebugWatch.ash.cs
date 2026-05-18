using Asharia.Behavior;

namespace com.game;

public class DebugWatch : BehaviorComponent
{
    public float Speed = 4.0f;

    protected override void Update(float delta)
    {
        var amount = GraphDebug.Inspect("amount", Speed * delta);
        GraphDebug.Watch("offset", new Vec3(0f, 0f, amount));
    }
}
