using ScriptLab;

namespace ScriptLab.Tests;

public sealed class BehaviorIrLowererTests
{
    [Fact]
    public void LowerFile_WhenPlayerMoveIsValid_ReturnsExpectedIrShape()
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath("PlayerMove.ash.cs"));

        Assert.Equal("com.game.PlayerMove", module.BehaviorId);

        var field = Assert.Single(module.Fields);
        Assert.Equal(new FieldId(1), field.FieldId);
        Assert.Equal("Speed", field.Name);
        Assert.Equal("float", field.Type);
        Assert.Equal("4.0f", field.InitialValue);

        var function = Assert.Single(module.Functions);
        Assert.Equal("Update", function.Name);

        var parameter = Assert.Single(function.Parameters);
        Assert.Equal("delta", parameter.Name);
        Assert.Equal("float", parameter.Type);

        Assert.Equal(new[] { "entry", "then_0", "exit_1" }, function.Blocks.Select(block => block.Name));

        var instructions = function.Blocks
            .SelectMany(block => block.Instructions)
            .Select(BehaviorIrText.Format)
            .ToArray();
        Assert.Contains("%0 = LoadEnum Key.W", instructions);
        Assert.Contains("%1 = Call asharia.input.keyDown(%0)", instructions);
        Assert.Contains("Branch %1 then then_0 else exit_1", instructions);
        Assert.Contains("%5 = LoadField #1 Speed", instructions);
        Assert.Contains("%6 = LoadLocal delta", instructions);
        Assert.Contains("%7 = BinaryOp Multiply %5, %6", instructions);
        Assert.Contains("%8 = MakeStruct Vec3(%3, %4, %7)", instructions);
        Assert.Contains("Call asharia.transform.translate(%2, %8)", instructions);

        var callInstruction = function.Blocks
            .SelectMany(block => block.Instructions)
            .OfType<BehaviorIrCallFunction>()
            .Single(call => call.FunctionId == new FunctionId("asharia.transform.translate"));
        Assert.Equal("PlayerMove.ash.cs", callInstruction.Source.FileName);
        Assert.True(callInstruction.Source.Line > 0);
        Assert.True(callInstruction.Source.Column > 0);
        Assert.StartsWith("ds_", callInstruction.DebugSiteId);
        Assert.Equal(BehaviorIrBreakabilityHint.Breakable, callInstruction.BreakabilityHint);

        var allInstructions = function.Blocks.SelectMany(block => block.Instructions).ToArray();
        Assert.All(allInstructions, instruction => Assert.StartsWith("ds_", instruction.DebugSiteId));
        Assert.Equal(allInstructions.Length, allInstructions.Select(instruction => instruction.DebugSiteId).Distinct().Count());
        Assert.Equal(
            BehaviorIrBreakabilityHint.Observable,
            allInstructions.OfType<BehaviorIrBinaryOp>().Single().BreakabilityHint);
    }

    [Fact]
    public void LowerFile_WhenGraphDebugWatchIsUsed_ReturnsDebugWatchInstructions()
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath("DebugWatch.ash.cs"));

        var watches = module.Functions
            .SelectMany(function => function.Blocks)
            .SelectMany(block => block.Instructions)
            .OfType<BehaviorIrDebugWatch>()
            .ToArray();

        Assert.Equal(new[] { "amount", "offset" }, watches.Select(watch => watch.Name));
        Assert.False(watches[0].IsStatement);
        Assert.True(watches[1].IsStatement);
    }

    [Theory]
    [InlineData("PlayerMove.ash.cs")]
    [InlineData("DebugWatch.ash.cs")]
    [InlineData("PrivateSerializedField.ash.cs")]
    public void LowerFile_WhenScriptIsValid_DoesNotEmitUnsupportedIr(string fileName)
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath(fileName));

        var unsupported = module.Functions
            .SelectMany(function => function.Blocks)
            .SelectMany(block => block.Instructions)
            .Where(instruction =>
                BehaviorIrText.Format(instruction).Contains("Unsupported", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(unsupported);
    }

    [Fact]
    public void LowerText_WhenIfElseIsUsed_BranchTargetsElseBlock()
    {
        var module = BehaviorIrLowerer.LowerText(
            BuildIfElseScript(),
            "IfElseMove.ash.cs");

        var function = Assert.Single(module.Functions);
        Assert.Equal(new[] { "entry", "then_0", "else_1", "exit_2" }, function.Blocks.Select(block => block.Name));

        var branch = Assert.Single(function.Blocks.SelectMany(block => block.Instructions).OfType<BehaviorIrBranch>());
        Assert.Equal("then_0", branch.ThenBlock);
        Assert.Equal("else_1", branch.ElseBlock);

        var result = BehaviorIrVerifier.ExecuteUpdate(
            module,
            0.016f,
            new HashSet<string>(StringComparer.Ordinal));
        var call = Assert.Single(result.Calls);
        Assert.Equal(new FunctionId("asharia.transform.translate"), call.FunctionId);
        Assert.Equal(new BehaviorIrVerificationVec3(0f, 0f, 2f), call.Arguments[1]);
    }

    [Fact]
    public void LowerText_WhenRegisteredFunctionUsesTypeAlias_UsesRegisteredFunctionId()
    {
        var module = BehaviorIrLowerer.LowerText(
            BuildAliasScript(),
            "AliasedInputMove.ash.cs");

        var call = module.Functions
            .SelectMany(function => function.Blocks)
            .SelectMany(block => block.Instructions)
            .OfType<BehaviorIrCallFunction>()
            .Single();

        Assert.Equal(new FunctionId("asharia.input.keyDown"), call.FunctionId);
    }

    [Fact]
    public void LowerText_WhenRegisteredValueTypeUsesTypeAlias_UsesCanonicalIrType()
    {
        var module = BehaviorIrLowerer.LowerText(
            BuildAliasValueTypeScript(),
            "AliasedOffsetMove.ash.cs");

        var makeStruct = module.Functions
            .SelectMany(function => function.Blocks)
            .SelectMany(block => block.Instructions)
            .OfType<BehaviorIrMakeStruct>()
            .Single();

        Assert.Equal("Vec3", makeStruct.Type);
    }

    [Fact]
    public void LowerText_WhenBehaviorFieldUsesTypeAlias_UsesCanonicalIrFieldType()
    {
        var module = BehaviorIrLowerer.LowerText(
            BuildAliasFieldScript(),
            "AliasedField.ash.cs");

        var field = Assert.Single(module.Fields);
        Assert.Equal("Vec3", field.Type);
    }

    [Fact]
    public void LowerText_WhenMethodParameterUsesTypeAlias_UsesCanonicalIrParameterType()
    {
        var module = BehaviorIrLowerer.LowerText(
            BuildAliasParameterScript(),
            "AliasedParameter.ash.cs");

        var parameter = Assert.Single(Assert.Single(module.Functions).Parameters);
        Assert.Equal("float", parameter.Type);
    }

    [Fact]
    public void LowerText_WhenLocalUsesTypeAlias_DeclaresCanonicalLocalType()
    {
        var module = BehaviorIrLowerer.LowerText(
            BuildAliasLocalScript(),
            "AliasedLocal.ash.cs");

        var declareLocal = module.Functions
            .SelectMany(function => function.Blocks)
            .SelectMany(block => block.Instructions)
            .OfType<BehaviorIrDeclareLocal>()
            .Single(instruction => instruction.LocalName == "offset");

        Assert.Equal("Vec3", declareLocal.Type);
    }

    [Fact]
    public void LowerText_WhenVarLocalUsesRegisteredValueType_DeclaresInferredCanonicalLocalType()
    {
        var module = BehaviorIrLowerer.LowerText(
            BuildVarLocalScript(),
            "InferredLocal.ash.cs");

        var declareLocal = module.Functions
            .SelectMany(function => function.Blocks)
            .SelectMany(block => block.Instructions)
            .OfType<BehaviorIrDeclareLocal>()
            .Single(instruction => instruction.LocalName == "offset");

        Assert.Equal("Vec3", declareLocal.Type);
    }

    [Fact]
    public void LowerText_WhenValueExpressionsAreLowered_CarriesCanonicalIrValueTypes()
    {
        var module = BehaviorIrLowerer.LowerText(
            BuildTypedValueScript(),
            "TypedValues.ash.cs");

        var instructions = module.Functions
            .SelectMany(function => function.Blocks)
            .SelectMany(block => block.Instructions)
            .ToArray();

        Assert.All(
            instructions.OfType<BehaviorIrLoadConst>(),
            loadConst => Assert.Equal("float", loadConst.Type));
        Assert.Equal("Key", instructions.OfType<BehaviorIrLoadEnum>().Single().Type);
        Assert.Equal("EntityRef", instructions.OfType<BehaviorIrLoadSelf>().Single().Type);
        Assert.Equal("float", instructions.OfType<BehaviorIrLoadField>().Single().Type);
        Assert.Equal("float", instructions
            .OfType<BehaviorIrLoadLocal>()
            .Single(loadLocal => loadLocal.LocalName == "delta")
            .Type);
        Assert.Equal("Vec3", instructions
            .OfType<BehaviorIrLoadLocal>()
            .Single(loadLocal => loadLocal.LocalName == "offset")
            .Type);
        Assert.Equal("float", instructions.OfType<BehaviorIrBinaryOp>().Single().Type);
        Assert.Equal("Vec3", instructions.OfType<BehaviorIrMakeStruct>().Single().Type);
        Assert.Equal("bool", instructions
            .OfType<BehaviorIrCallFunction>()
            .Single(call => call.Target is not null)
            .ReturnType);
        Assert.Null(instructions
            .OfType<BehaviorIrCallFunction>()
            .Single(call => call.Target is null)
            .ReturnType);
    }

    private static string GetSamplePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Samples", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate sample script '{fileName}'.");
    }

    private static string BuildIfElseScript()
    {
        return """
            using Asharia.Behavior;

            namespace com.game;

            public class IfElseMove : BehaviorComponent
            {
                protected override void Update(float delta)
                {
                    if (Input.KeyDown(Key.W))
                    {
                        Transform.Translate(Self, new Vec3(0f, 0f, 1f));
                    }
                    else
                    {
                        Transform.Translate(Self, new Vec3(0f, 0f, 2f));
                    }
                }
            }
            """;
    }

    private static string BuildAliasScript()
    {
        return """
            using Asharia.Behavior;
            using GameInput = Asharia.Behavior.Input;

            namespace com.game;

            public class AliasedInputMove : BehaviorComponent
            {
                protected override void Update(float delta)
                {
                    if (GameInput.KeyDown(Key.W))
                    {
                        return;
                    }
                }
            }
            """;
    }

    private static string BuildAliasValueTypeScript()
    {
        return """
            using Asharia.Behavior;
            using Offset = Asharia.Behavior.Vec3;

            namespace com.game;

            public class AliasedOffsetMove : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(float delta)
                {
                    Transform.Translate(Self, new Offset(0f, 0f, Speed * delta));
                }
            }
            """;
    }

    private static string BuildAliasFieldScript()
    {
        return """
            using Asharia.Behavior;
            using Offset = Asharia.Behavior.Vec3;

            namespace com.game;

            public class AliasedField : BehaviorComponent
            {
                [Field(1)]
                public Offset SpawnOffset;

                protected override void Update(float delta)
                {
                    return;
                }
            }
            """;
    }

    private static string BuildAliasParameterScript()
    {
        return """
            using Asharia.Behavior;
            using Delta = System.Single;

            namespace com.game;

            public class AliasedParameter : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(Delta delta)
                {
                    return;
                }
            }
            """;
    }

    private static string BuildAliasLocalScript()
    {
        return """
            using Asharia.Behavior;
            using Offset = Asharia.Behavior.Vec3;

            namespace com.game;

            public class AliasedLocal : BehaviorComponent
            {
                protected override void Update(float delta)
                {
                    Offset offset = new Offset(0f, 0f, delta);
                    Transform.Translate(Self, offset);
                }
            }
            """;
    }

    private static string BuildVarLocalScript()
    {
        return """
            using Asharia.Behavior;

            namespace com.game;

            public class InferredLocal : BehaviorComponent
            {
                protected override void Update(float delta)
                {
                    var offset = new Vec3(0f, 0f, delta);
                    Transform.Translate(Self, offset);
                }
            }
            """;
    }

    private static string BuildTypedValueScript()
    {
        return """
            using Asharia.Behavior;
            using Offset = Asharia.Behavior.Vec3;

            namespace com.game;

            public class TypedValues : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(float delta)
                {
                    Offset offset = new Offset(0f, 0f, Speed * delta);
                    if (Input.KeyDown(Key.W))
                    {
                        Transform.Translate(Self, offset);
                    }
                }
            }
            """;
    }
}
