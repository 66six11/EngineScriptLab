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
    [InlineData("switch (Speed) { default: return; }", "AGC0001")]
    [InlineData("while (Input.KeyDown(Key.W)) { return; }", "AGC0007")]
    [InlineData("var values = from value in Numbers select value;", "AGC0002")]
    [InlineData("var pressed = !Input.KeyDown(Key.W);", "AGC0002")]
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
    public void ParseText_WhenRegisteredFunctionUsesTypeAlias_ReturnsNoDiagnostics()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            using Asharia.Behavior;
            using GameInput = Asharia.Behavior.Input;

            namespace com.game;

            [Behavior("com.game.AliasedInputMove")]
            public sealed partial class AliasedInputMove : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(float delta)
                {
                    if (GameInput.KeyDown(Key.W))
                    {
                        return;
                    }
                }
            }
            """,
            "AliasedInputMove.ash.cs");

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ParseText_WhenLocalTypeSpoofsRegisteredFunctionName_ReturnsAgc0003()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            namespace com.game;

            public enum Key
            {
                W
            }

            public static class Input
            {
                public static bool KeyDown(Key key) => true;
            }

            [Asharia.Behavior.Behavior("com.game.SpoofedInputMove")]
            public sealed partial class SpoofedInputMove : Asharia.Behavior.BehaviorComponent
            {
                [Asharia.Behavior.Field(1)]
                public float Speed = 4.0f;

                protected override void Update(float delta)
                {
                    if (Input.KeyDown(Key.W))
                    {
                        return;
                    }
                }
            }
            """,
            "SpoofedInputMove.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0003" &&
            diagnostic.Message.Contains("com.game.Input.KeyDown", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_WhenRegisteredValueTypeUsesTypeAlias_ReturnsNoDiagnostics()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            using Asharia.Behavior;
            using Offset = Asharia.Behavior.Vec3;

            namespace com.game;

            [Behavior("com.game.AliasedOffsetMove")]
            public sealed partial class AliasedOffsetMove : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(float delta)
                {
                    Transform.Translate(Self, new Offset(0f, 0f, Speed * delta));
                }
            }
            """,
            "AliasedOffsetMove.ash.cs");

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ParseText_WhenLocalTypeSpoofsRegisteredValueType_ReturnsAgc0009()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            using Asharia.Behavior;

            namespace com.game;

            public sealed class Vec3
            {
                public Vec3(float x, float y, float z)
                {
                }
            }

            [Behavior("com.game.SpoofedVec3Move")]
            public sealed partial class SpoofedVec3Move : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(float delta)
                {
                    Transform.Translate(Self, new Vec3(0f, 0f, Speed * delta));
                }
            }
            """,
            "SpoofedVec3Move.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0009" &&
            diagnostic.Message.Contains("com.game.Vec3", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_WhenBehaviorFieldUsesRegisteredTypeAlias_ReturnsNoDiagnostics()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            using Asharia.Behavior;
            using Offset = Asharia.Behavior.Vec3;

            namespace com.game;

            [Behavior("com.game.AliasedField")]
            public sealed partial class AliasedField : BehaviorComponent
            {
                [Field(1)]
                public Offset SpawnOffset;

                protected override void Update(float delta)
                {
                    return;
                }
            }
            """,
            "AliasedField.ash.cs");

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);

        Assert.NotNull(result.Behavior);
        var field = Assert.Single(result.Behavior.Fields);
        Assert.Equal("Vec3", field.Type);
    }

    [Fact]
    public void ParseText_WhenMethodParameterUsesTypeAlias_ReturnsCanonicalSignature()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            using Asharia.Behavior;
            using Delta = System.Single;

            namespace com.game;

            [Behavior("com.game.AliasedParameter")]
            public sealed partial class AliasedParameter : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(Delta delta)
                {
                    return;
                }
            }
            """,
            "AliasedParameter.ash.cs");

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);

        Assert.NotNull(result.Behavior);
        var method = Assert.Single(result.Behavior.Methods);
        Assert.Equal("Update(float delta)", method.Signature);
    }

    [Fact]
    public void ParseText_WhenBehaviorFieldUsesCustomType_ReturnsAgc0008()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            using Asharia.Behavior;

            namespace com.game;

            public sealed class SpawnSettings
            {
            }

            [Behavior("com.game.CustomField")]
            public sealed partial class CustomField : BehaviorComponent
            {
                [Field(1)]
                public SpawnSettings Settings;

                protected override void Update(float delta)
                {
                    return;
                }
            }
            """,
            "CustomField.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0008" &&
            diagnostic.Message.Contains("com.game.SpawnSettings", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_WhenBehaviorFieldSpoofsRegisteredTypeName_ReturnsAgc0008()
    {
        var result = GraphCSharpScriptParser.ParseText(
            """
            using Asharia.Behavior;

            namespace com.game;

            public sealed class Vec3
            {
            }

            [Behavior("com.game.SpoofedField")]
            public sealed partial class SpoofedField : BehaviorComponent
            {
                [Field(1)]
                public Vec3 Offset;

                protected override void Update(float delta)
                {
                    return;
                }
            }
            """,
            "SpoofedField.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0008" &&
            diagnostic.Message.Contains("com.game.Vec3", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_WhenRegisteredFunctionArgumentCountDoesNotMatch_ReturnsAgc0008()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScript("if (Input.KeyDown()) { return; }"),
            "InvalidArgumentCount.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0008" &&
            diagnostic.Message.Contains("expects 1 argument", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_WhenRegisteredFunctionArgumentTypeDoesNotMatch_ReturnsAgc0008()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScript("Transform.Translate(Self, 1);"),
            "InvalidArgumentType.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0008" &&
            diagnostic.Message.Contains("expects 'Vec3'", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("int", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_WhenRegisteredFunctionIsCalledFromIllegalContext_ReturnsAgc0005()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScriptWithMethod(
                "Start",
                "if (Input.KeyDown(Key.W)) { return; }"),
            "IllegalContext.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0005" &&
            diagnostic.Message.Contains("Input.KeyDown", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("Start", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_WhenRegisteredFunctionIsCalledFromUnknownContext_ReturnsAgc0005()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScriptWithMethod(
                "Tick",
                "if (Input.KeyDown(Key.W)) { return; }"),
            "UnknownContext.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0005" &&
            diagnostic.Message.Contains("Input.KeyDown", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseText_WhenWorldMutationIsHiddenInsideExpression_ReturnsAgc0006()
    {
        var result = GraphCSharpScriptParser.ParseText(
            BuildScript("var text = Transform.Translate(Self, new Vec3(0f, 0f, 1f)).ToString();"),
            "HiddenSideEffect.ash.cs");

        Assert.True(result.HasErrors);
        Assert.False(result.HasSyntaxErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Id == "AGC0006" &&
            diagnostic.Message.Contains("Transform.Translate", StringComparison.Ordinal));
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

    private static string BuildScriptWithMethod(string methodName, string methodBody)
    {
        return $$"""
            using Asharia.Behavior;

            namespace com.game;

            [Behavior("com.game.Unsupported")]
            public sealed partial class Unsupported : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                private void {{methodName}}()
                {
                    {{methodBody}}
                }
            }
            """;
    }
}
