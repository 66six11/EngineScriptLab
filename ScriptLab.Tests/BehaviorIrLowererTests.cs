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

        var function = Assert.Single(module.Functions);
        Assert.Equal("Update", function.Name);

        var parameter = Assert.Single(function.Parameters);
        Assert.Equal("delta", parameter.Name);
        Assert.Equal("float", parameter.Type);

        Assert.Equal(new[] { "entry", "then_0", "exit_1" }, function.Blocks.Select(block => block.Name));

        var instructions = function.Blocks.SelectMany(block => block.Instructions).ToArray();
        Assert.Contains("%0 = LoadEnum Key.W", instructions);
        Assert.Contains("%1 = Call asharia.input.keyDown(%0)", instructions);
        Assert.Contains("Branch %1 then then_0 else exit_1", instructions);
        Assert.Contains("%5 = LoadField Speed", instructions);
        Assert.Contains("%6 = LoadLocal delta", instructions);
        Assert.Contains("%7 = BinaryOp Multiply %5, %6", instructions);
        Assert.Contains("%8 = MakeStruct Vec3(%3, %4, %7)", instructions);
        Assert.Contains("Call asharia.transform.translate(%2, %8)", instructions);
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
}
