using ScriptLab;

namespace ScriptLab.Tests;

public sealed class GraphCSharpScriptParserTests
{
    [Fact]
    public void ParseFile_WhenPlayerMoveIsValid_ReturnsExpectedSummary()
    {
        var result = GraphCSharpScriptParser.ParseFile(GetSamplePath("PlayerMove.ash.cs"));

        Assert.Empty(result.Diagnostics);
        Assert.False(result.HasSyntaxErrors);
        Assert.NotNull(result.Behavior);

        var behavior = result.Behavior;
        Assert.Equal("PlayerMove", behavior.Name);
        Assert.Equal("com.game.PlayerMove", behavior.Id);
        Assert.Equal("explicit", behavior.IdSource);
        Assert.Empty(behavior.FormerlyBehaviorIds);

        var field = Assert.Single(behavior.Fields);
        Assert.Equal("Speed", field.Name);
        Assert.Equal(new FieldId(1), field.FieldId);
        Assert.Equal("float", field.Type);
        Assert.Equal("public", field.Accessibility);
        Assert.Equal("explicit", field.Serialization);

        var method = Assert.Single(behavior.Methods);
        Assert.Equal("Update", method.Name);
        Assert.Equal("Update(float delta)", method.Signature);

        var ifNode = Assert.Single(method.Body);
        Assert.Equal("If", ifNode.Kind);
        Assert.Equal("Input.KeyDown(Key.W)", ifNode.Text);

        var callNode = Assert.Single(ifNode.Children);
        Assert.Equal("Call", callNode.Kind);
        Assert.Equal("Transform.Translate(Self, new Vec3(0f, 0f, Speed * delta))", callNode.Text);
    }

    [Fact]
    public void ParseFile_WhenScriptHasInvalidSyntax_ReturnsSyntaxDiagnostic()
    {
        var result = GraphCSharpScriptParser.ParseFile(GetSamplePath("InvalidSyntax.ash.cs"));

        Assert.NotEmpty(result.Diagnostics);
        Assert.True(result.HasErrors);
        Assert.True(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == "Error");
    }

    [Theory]
    [InlineData("var predicate = () => true;", "AGC0001")]
    [InlineData("await MoveAsync();", "AGC0001")]
    [InlineData("try { return; } catch { return; }", "AGC0001")]
    [InlineData("while (Input.KeyDown(Key.W)) { return; }", "AGC0007")]
    [InlineData("var values = from value in Numbers select value;", "AGC0002")]
    [InlineData("dynamic value = 1;", "AGC0008")]
    [InlineData("Task value = null;", "AGC0008")]
    [InlineData("var members = typeof(Unsupported).GetMethods();", "AGC0002")]
    [InlineData("var value = new Random();", "AGC0009")]
    [InlineData("Vec3 value = new(0f, 0f, 1f);", "AGC0009")]
    [InlineData("var values = new[] { 1, 2 };", "AGC0009")]
    public void ParseText_WhenScriptUsesUnsupportedGraphCSharpSyntax_ReturnsAgcDiagnostic(
        string updateBody,
        string expectedDiagnosticId)
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScript(updateBody),
            $"Unsupported_{expectedDiagnosticId}.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == expectedDiagnosticId && diagnostic.Severity == "Error");
    }

    [Fact]
    public void ParseText_WhenScriptCallsUnregisteredMemberFunction_ReturnsAgc0003()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScript("""Debug.Log("move");"""),
            "UnregisteredCall.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0003" &&
            diagnostic.Message.Contains("Debug.Log", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_WhenScriptDeclaresStaticField_ReturnsAgc0001()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScriptWithFields("private static float Cache;"),
            "StaticField.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0001" &&
            diagnostic.Message.Contains("Static field 'Cache'", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseFile_WhenScriptUsesGraphDebugWatch_ReturnsNoDiagnostics()
    {
        var result = GraphCSharpScriptParser.ParseFile(GetSamplePath("DebugWatch.ash.cs"));

        Assert.Empty(result.Diagnostics);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void ParseText_WhenPublicFieldHasNoStableFieldId_ReturnsAgc0004()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScriptWithFields("public float Speed = 4.0f;"),
            "PublicField.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "AGC0004");
    }

    [Fact]
    public void ParseText_WhenPrivateFieldHasStableFieldId_ReturnsFieldSummary()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScriptWithFields("""
                [Field(1)]
                private float speed = 4.0f;
                """),
            "PrivateSerializedField.ash.cs");

        Assert.False(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);

        Assert.NotNull(result.Behavior);

        var field = Assert.Single(result.Behavior.Fields);
        Assert.Equal("speed", field.Name);
        Assert.Equal(new FieldId(1), field.FieldId);
        Assert.Equal("private", field.Accessibility);
        Assert.Equal("explicit", field.Serialization);
    }

    [Theory]
    [InlineData("[Field]")]
    [InlineData("[SerializeField]")]
    public void ParseText_WhenSerializedFieldHasNoStableFieldId_ReturnsAgc0004(string attribute)
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScriptWithFields($$"""
                {{attribute}}
                private float speed = 4.0f;
                """),
            "MissingFieldId.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "AGC0004");
    }

    [Fact]
    public void ParseText_WhenPrivateFieldHasNoSerializationAttribute_DoesNotReturnFieldSummary()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScriptWithFields("private float speed = 4.0f;"),
            "PrivateHelperField.ash.cs");

        Assert.False(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.NotNull(result.Behavior);
        Assert.Empty(result.Behavior.Fields);
    }

    [Fact]
    public void ParseText_WhenBehaviorAttributeExists_UsesExplicitBehaviorId()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            using Asharia.Behavior;

            namespace game.scripts;

            [Behavior("com.game.CustomMove")]
            public class PlayerMove : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;
            }
            """,
            "ExplicitBehaviorId.ash.cs");

        Assert.False(result.HasErrors);
        Assert.NotNull(result.Behavior);
        Assert.Equal("com.game.CustomMove", result.Behavior.Id);
        Assert.Equal("explicit", result.Behavior.IdSource);
    }

    [Fact]
    public void ParseText_WhenFormerlyBehaviorExists_ReturnsMigrationIds()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            using Asharia.Behavior;

            namespace com.game;

            [FormerlyBehavior("com.game.OldMove")]
            [FormerlyBehavior("com.game.LegacyMove")]
            public class PlayerMove : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;
            }
            """,
            "FormerlyBehavior.ash.cs");

        Assert.False(result.HasErrors);
        Assert.NotNull(result.Behavior);
        Assert.Equal("com.game.PlayerMove", result.Behavior.Id);
        Assert.Equal(new[] { "com.game.OldMove", "com.game.LegacyMove" }, result.Behavior.FormerlyBehaviorIds);
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

    private static string BuildScript(string updateBody)
    {
        return $$"""
            using Asharia.Behavior;

            namespace com.game;

            [Behavior("com.game.Unsupported")]
            public sealed partial class Unsupported : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(float delta)
                {
                    {{updateBody}}
                }
            }
            """;
    }

    private static string BuildScriptWithFields(string fields)
    {
        return $$"""
            using Asharia.Behavior;

            namespace com.game;

            public class FieldDiscovery : BehaviorComponent
            {
                {{fields}}

                protected override void Update(float delta)
                {
                    return;
                }
            }
            """;
    }
}
