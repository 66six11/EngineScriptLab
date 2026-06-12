using ScriptLab;

namespace ScriptLab.Tests;

public sealed class GraphSourceRewriterTests
{
    [Fact]
    public void ReplaceEnumMember_WhenKeyNodeIsEdited_ReprojectsGraphWithNewKey()
    {
        var path = GetSamplePath("PlayerMove.ash.cs");
        var source = File.ReadAllText(path);
        var graph = Project(source, path);
        var function = Assert.Single(graph.Functions);
        var keyNode = FindNode(function, "Enum", "Key.W");
        var identity = GraphNodeIdentityFactory.FromNode(source, function.Name, keyNode, function.Nodes);

        var result = GraphSourceRewriter.ReplaceEnumMember(source, identity, "Key.S");

        Assert.True(result.Applied, result.Message);
        Assert.Contains("Input.KeyDown(Key.S)", result.Source, StringComparison.Ordinal);

        var rewrittenGraph = Project(result.Source, path);
        var rewrittenFunction = Assert.Single(rewrittenGraph.Functions);
        FindNode(rewrittenFunction, "Enum", "Key.S");
        Assert.DoesNotContain(rewrittenFunction.Nodes, node => node.Kind == "Enum" && node.Label == "Key.W");
    }

    [Fact]
    public void ReplaceLiteral_WhenFieldInitializerIsEdited_ReprojectsGraph()
    {
        var path = GetSamplePath("PlayerMove.ash.cs");
        var source = File.ReadAllText(path);
        var literalText = "4.0f";
        var literalStart = source.IndexOf(literalText, StringComparison.Ordinal);
        Assert.True(literalStart >= 0);
        var identity = GraphNodeIdentity.FromSource(
            "PlayerMove",
            "Const",
            null,
            source,
            path,
            literalStart,
            literalText.Length);

        var result = GraphSourceRewriter.ReplaceLiteral(source, identity, "8.0f");

        Assert.True(result.Applied, result.Message);
        Assert.Contains("public float Speed = 8.0f;", result.Source, StringComparison.Ordinal);

        var module = LowerAndProject(result.Source, path, out var graph);
        Assert.Equal("8.0f", Assert.Single(module.Fields).InitialValue);
        Assert.Single(graph.Functions);
    }

    [Fact]
    public void InsertStatementAfter_WhenCallNodeIsAnchored_AddsWatchStatement()
    {
        var path = GetSamplePath("PlayerMove.ash.cs");
        var source = File.ReadAllText(path);
        var graph = Project(source, path);
        var function = Assert.Single(graph.Functions);
        var translateNode = FindNode(function, "Call", "asharia.transform.translate");
        var identity = GraphNodeIdentityFactory.FromNode(source, function.Name, translateNode, function.Nodes);

        var result = GraphSourceRewriter.InsertStatementAfter(
            source,
            identity,
            """GraphDebug.Watch("speed", Speed);""");

        Assert.True(result.Applied, result.Message);
        Assert.Contains("""GraphDebug.Watch("speed", Speed);""", result.Source, StringComparison.Ordinal);

        var rewrittenGraph = Project(result.Source, path);
        var rewrittenFunction = Assert.Single(rewrittenGraph.Functions);
        FindNode(rewrittenFunction, "Watch", "speed");
    }

    [Fact]
    public void ReorderStatements_WhenTwoStatementsAreAnchored_SwapsTheirSourceOrder()
    {
        var source = BuildTwoWatchScript();
        var path = "TwoWatch.ash.cs";
        var graph = Project(source, path);
        var function = Assert.Single(graph.Functions);
        var firstNode = FindNode(function, "Watch", "first");
        var secondNode = FindNode(function, "Watch", "second");
        var firstIdentity = GraphNodeIdentityFactory.FromNode(source, function.Name, firstNode, function.Nodes);
        var secondIdentity = GraphNodeIdentityFactory.FromNode(source, function.Name, secondNode, function.Nodes);

        var result = GraphSourceRewriter.ReorderStatements(source, firstIdentity, secondIdentity);

        Assert.True(result.Applied, result.Message);
        Assert.True(
            result.Source.IndexOf("\"second\"", StringComparison.Ordinal) <
            result.Source.IndexOf("\"first\"", StringComparison.Ordinal));

        var rewrittenGraph = Project(result.Source, path);
        var rewrittenFunction = Assert.Single(rewrittenGraph.Functions);
        Assert.Equal(
            new[] { "second", "first" },
            rewrittenFunction.Nodes
                .Where(node => node.Kind == "Watch")
                .Select(node => node.Label)
                .ToArray());
    }

    private static BlueprintGraphModule Project(string source, string path)
    {
        var module = LowerAndProject(source, path, out var graph);
        Assert.NotNull(module);
        return graph;
    }

    private static BehaviorIrModule LowerAndProject(
        string source,
        string path,
        out BlueprintGraphModule graph)
    {
        var parse = GraphCSharpScriptParser.ParseText(source, path);
        Assert.Empty(parse.Diagnostics);
        var module = BehaviorIrLowerer.LowerText(source, path);
        graph = BlueprintGraphProjector.Project(module);
        return module;
    }

    private static BlueprintGraphNode FindNode(BlueprintGraphFunction function, string kind, string label)
    {
        return function.Nodes.Single(node => node.Kind == kind && node.Label == label);
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

    private static string BuildTwoWatchScript()
    {
        return """
            using Asharia.Behavior;

            namespace com.game;

            [Behavior("com.game.TwoWatch")]
            public sealed partial class TwoWatch : BehaviorComponent
            {
                [Field(1)]
                public float Speed = 4.0f;

                protected override void Update(float delta)
                {
                    GraphDebug.Watch("first", Speed);
                    GraphDebug.Watch("second", delta);
                }
            }
            """;
    }
}
