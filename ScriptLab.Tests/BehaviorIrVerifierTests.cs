using ScriptLab;

namespace ScriptLab.Tests;

public sealed class BehaviorIrVerifierTests
{
    [Fact]
    public void ExecuteUpdate_WhenWIsDown_RecordsTranslateCall()
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath("PlayerMove.ash.cs"));

        var result = BehaviorIrVerifier.ExecuteUpdate(
            module,
            delta: 0.016f,
            downKeys: new HashSet<string>(StringComparer.Ordinal) { "Key.W" });

        var call = Assert.Single(result.Calls);
        Assert.Equal(new FunctionId("asharia.transform.translate"), call.FunctionId);
        Assert.Equal("Self", call.Arguments[0]);

        var offset = Assert.IsType<BehaviorIrVerificationVec3>(call.Arguments[1]);
        Assert.Equal(0f, offset.X);
        Assert.Equal(0f, offset.Y);
        Assert.Equal(0.064f, offset.Z, precision: 4);
    }

    [Fact]
    public void ExecuteUpdate_WhenWIsNotDown_DoesNotRecordTranslateCall()
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath("PlayerMove.ash.cs"));

        var result = BehaviorIrVerifier.ExecuteUpdate(
            module,
            delta: 0.016f,
            downKeys: new HashSet<string>(StringComparer.Ordinal));

        Assert.Empty(result.Calls);
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
