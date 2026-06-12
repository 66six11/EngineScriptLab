using Asharia.Behavior;

namespace com.game;

[Behavior("com.game.UnsupportedLoop")] public sealed partial class UnsupportedLoop : BehaviorComponent
{
    [Field(1)]
    public float Speed = 4.0f;

    protected override void Update(float delta)
    {
        while (Input.KeyDown(Key.W))
        {
            Transform.Translate(Self, new Vec3(0f, 0f, Speed * delta));
        }
    }
}
