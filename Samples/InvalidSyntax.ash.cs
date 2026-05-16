using Asharia.Behavior;

[Behavior("com.game.InvalidSyntax")]
public sealed partial class InvalidSyntax : BehaviorComponent
{
    public float Speed = 4.0f;

    protected override void Update(float delta)
    {
        if (Input.KeyDown(Key.W)
        {
            Transform.Translate(Self, new Vec3(0f, 0f, Speed * delta));
        }
    }
}
