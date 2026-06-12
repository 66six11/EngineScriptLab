using ScriptLab;

namespace ScriptLab.Tests;

public sealed class BehaviorInterpreterTests
{
    [Fact]
    public void ExecuteEvent_WhenWIsDown_EnqueuesTranslateMutation()
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath("PlayerMove.ash.cs"));
        var program = BehaviorProgram.FromModule(module);
        var instance = ScriptInstance.Create(program, entityId: 1);
        var context = new ScriptExecutionContext(
            downKeys: new HashSet<string>(StringComparer.Ordinal) { "Key.W" },
            validEntityIds: new HashSet<int> { 1 });

        var result = new BehaviorInterpreter().ExecuteEvent(
            program,
            instance,
            "Update",
            new Dictionary<string, object?> { ["delta"] = 0.016f },
            context);

        Assert.Empty(result.Diagnostics);

        var mutation = Assert.IsType<TranslateMutation>(Assert.Single(result.Mutations));
        Assert.Equal(new FunctionId("asharia.transform.translate"), mutation.FunctionId);
        Assert.Equal(1, mutation.Entity.EntityId);
        Assert.Equal(0f, mutation.Offset.X);
        Assert.Equal(0f, mutation.Offset.Y);
        Assert.Equal(0.064f, mutation.Offset.Z, precision: 4);
        Assert.StartsWith("ds_", mutation.SourceSiteId);
    }

    [Fact]
    public void ExecuteEvent_WhenTargetEntityIsInvalid_RecordsRuntimeDiagnostic()
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath("PlayerMove.ash.cs"));
        var program = BehaviorProgram.FromModule(module);
        var instance = ScriptInstance.Create(program, entityId: 1);
        var context = new ScriptExecutionContext(
            downKeys: new HashSet<string>(StringComparer.Ordinal) { "Key.W" },
            validEntityIds: new HashSet<int>());

        var result = new BehaviorInterpreter().ExecuteEvent(
            program,
            instance,
            "Update",
            new Dictionary<string, object?> { ["delta"] = 0.016f },
            context);

        Assert.Empty(result.Mutations);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(RuntimeDiagnostic.InvalidEntity, diagnostic.Code);
        Assert.Contains("invalid entity", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(diagnostic.DebugSiteId?.StartsWith("ds_", StringComparison.Ordinal) == true);
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
