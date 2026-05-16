using Asharia.Behavior;

namespace com.game;

public class UnregisteredCall : BehaviorComponent
{
    public float Speed = 4.0f;

    protected override void Update(float delta)
    {
        Debug.Log("move");
    }
}
