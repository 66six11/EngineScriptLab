using Asharia.Behavior;

namespace com.game;

public class PrivateSerializedField : BehaviorComponent
{
    [Field]
    private float speed = 4.0f;

    protected override void Update(float delta)
    {
        Transform.Translate(Self, new Vec3(0f, 0f, speed * delta));
    }
}
