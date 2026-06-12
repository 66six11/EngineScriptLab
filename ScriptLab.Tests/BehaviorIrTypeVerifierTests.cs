namespace ScriptLab.Tests;

public sealed class BehaviorIrTypeVerifierTests
{
    private static readonly BehaviorSourceSpan Source = BehaviorSourceSpan.Generated;

    [Fact]
    public void Verify_WhenBranchConditionIsNotBool_Throws()
    {
        var module = CreateModule(
            new BehaviorIrLoadConst("%0", "float", "1f", Source),
            new BehaviorIrBranch("%0", "then_0", "exit_1", Source));

        var exception = Assert.Throws<InvalidOperationException>(() => BehaviorIrTypeVerifier.Verify(module));

        Assert.Contains("Branch condition expects 'bool'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_WhenStoreLocalValueTypeDoesNotMatch_Throws()
    {
        var module = CreateModule(
            new BehaviorIrDeclareLocal("amount", "float", Source),
            new BehaviorIrLoadEnum("%0", "Key", "Key.W", Source),
            new BehaviorIrStoreLocal("amount", "%0", Source));

        var exception = Assert.Throws<InvalidOperationException>(() => BehaviorIrTypeVerifier.Verify(module));

        Assert.Contains("StoreLocal 'amount' expects 'float'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_WhenCallArgumentTypeDoesNotMatchBinding_Throws()
    {
        var module = CreateModule(
            new BehaviorIrLoadConst("%0", "int", "1", Source),
            new BehaviorIrCallFunction(
                null,
                null,
                new FunctionId("asharia.input.keyDown"),
                new[] { "%0" },
                Source));

        var exception = Assert.Throws<InvalidOperationException>(() => BehaviorIrTypeVerifier.Verify(module));

        Assert.Contains("argument 1 expects 'Key'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_WhenInstructionReferencesUnknownTemp_Throws()
    {
        var module = CreateModule(new BehaviorIrDebugWatch("value", "%missing", false, Source));

        var exception = Assert.Throws<InvalidOperationException>(() => BehaviorIrTypeVerifier.Verify(module));

        Assert.Contains("unknown temp '%missing'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_WhenValidLoweredIrIsUsed_DoesNotThrow()
    {
        var module = BehaviorIrLowerer.LowerText(
            """
            using Asharia.Behavior;

            namespace com.game;

            public class ValidTypedIr : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(float delta)
                {
                    if (Input.KeyDown(Key.W))
                    {
                        Transform.Translate(Self, new Vec3(0f, 0f, Speed * delta));
                    }
                }
            }
            """,
            "ValidTypedIr.ash.cs");

        BehaviorIrTypeVerifier.Verify(module);
    }

    private static BehaviorIrModule CreateModule(params BehaviorIrInstruction[] instructions)
    {
        return new BehaviorIrModule(
            "com.game.InvalidIr",
            Array.Empty<BehaviorIrField>(),
            new[]
            {
                new BehaviorIrFunction(
                    "Update",
                    Array.Empty<BehaviorIrParameter>(),
                    new[]
                    {
                        new BehaviorIrBlock("entry", instructions),
                        new BehaviorIrBlock("then_0", Array.Empty<BehaviorIrInstruction>()),
                        new BehaviorIrBlock("exit_1", Array.Empty<BehaviorIrInstruction>())
                    })
            });
    }
}
