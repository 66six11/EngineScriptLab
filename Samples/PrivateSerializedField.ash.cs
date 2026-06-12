using Asharia.Behavior;

namespace com.game;

[Behavior("com.game.PrivateSerializedField")] public sealed partial class PrivateSerializedField : BehaviorComponent
{
    [Field(1)]
    private float speed = 4.0f;

    protected override void Update(float delta)
    {
        Transform.Translate(Self, new Vec3(0f, 0f, speed * delta));
    }
}
