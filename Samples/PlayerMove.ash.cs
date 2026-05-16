using Asharia.Behavior;

[Behavior("com.game.PlayerMove")]
public sealed partial class PlayerMove : BehaviorComponent
{
    [Range(0f, 20f)]
    public float Speed = 4.0f;

    protected override void Update(float delta)
    {
        if (Input.KeyDown(Key.W))
        {
            Transform.Translate(Self, new Vec3(0f, 0f, Speed * delta));
        }
    }
}
