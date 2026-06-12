using Asharia.Behavior;

namespace com.game;

[Behavior("com.game.UnregisteredCall")] public sealed partial class UnregisteredCall : BehaviorComponent
{
    [Field(1)]
    public float Speed = 4.0f;

    protected override void Update(float delta)
    {
        Debug.Log("move");
    }
}
