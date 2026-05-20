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
        Assert.Contains("%5 = LoadField Speed", instructions);
        Assert.Contains("%6 = LoadLocal delta", instructions);
        Assert.Contains("%7 = BinaryOp Multiply %5, %6", instructions);
        Assert.Contains("%8 = MakeStruct Vec3(%3, %4, %7)", instructions);
        Assert.Contains("Call asharia.transform.translate(%2, %8)", instructions);

        var callInstruction = function.Blocks
            .SelectMany(block => block.Instructions)
            .OfType<BehaviorIrCallFunction>()
            .Single(call => call.FunctionId == "asharia.transform.translate");
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
        Assert.Equal("asharia.transform.translate", call.FunctionId);
        Assert.Equal(new BehaviorIrVerificationVec3(0f, 0f, 2f), call.Arguments[1]);
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
}
