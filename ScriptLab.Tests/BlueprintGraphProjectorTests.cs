using ScriptLab;
using System.Text.Json;

namespace ScriptLab.Tests;

public sealed class BlueprintGraphProjectorTests
{
    [Fact]
    public void Project_WhenPlayerMoveIrIsValid_ReturnsExpectedGraphShape()
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath("PlayerMove.ash.cs"));
        var graph = BlueprintGraphProjector.Project(module);

        Assert.Equal("com.game.PlayerMove", graph.BehaviorId);

        var function = Assert.Single(graph.Functions);
        Assert.Equal("Update", function.Name);
        var parameter = Assert.Single(function.Parameters);
        Assert.Equal("delta", parameter.Name);
        Assert.Equal("float", parameter.Type);

        var eventNode = FindNode(function, "Event", "Update");
        var keyNode = FindNode(function, "Enum", "Key.W");
        var keyDownNode = FindNode(function, "Call", "asharia.input.keyDown");
        var branchNode = FindNode(function, "Branch", "Branch");
        var speedNode = FindNode(function, "GetField", "Speed");
        var multiplyNode = FindNode(function, "BinaryOp", "Multiply");
        var vec3Node = FindNode(function, "MakeStruct", "Vec3");
        var translateNode = FindNode(function, "Call", "asharia.transform.translate");
        Assert.Equal("PlayerMove.ash.cs", translateNode.Source.FileName);
        Assert.True(translateNode.Source.Line > 0);
        Assert.StartsWith("ds_", branchNode.DebugSiteId);
        Assert.Equal(BehaviorIrBreakabilityHint.Breakable, branchNode.BreakabilityHint);
        Assert.False(branchNode.BreakableVerified);
        Assert.True(branchNode.Observable);
        Assert.Equal(branchNode.DebugSiteId, branchNode.OwningBreakableDebugSiteId);
        Assert.Equal(BehaviorIrBreakabilityHint.Observable, keyDownNode.BreakabilityHint);
        Assert.Equal(branchNode.DebugSiteId, keyDownNode.OwningBreakableDebugSiteId);
        Assert.Equal(BehaviorIrBreakabilityHint.Breakable, translateNode.BreakabilityHint);
        Assert.Equal(translateNode.DebugSiteId, translateNode.OwningBreakableDebugSiteId);
        Assert.DoesNotContain(function.Nodes, node => node.Kind == "GetLocal" && node.Label == "delta");

        Assert.Contains(function.Edges, edge =>
            edge.Kind == "data" &&
            edge.From == keyNode.Id &&
            edge.To == keyDownNode.Id &&
            edge.Label == "arg0" &&
            edge.FromPin == "value" &&
            edge.ToPin == "arg0");
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "data" && edge.From == keyDownNode.Id && edge.To == branchNode.Id && edge.Label == "condition");
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "exec" && edge.From == eventNode.Id && edge.To == branchNode.Id && edge.Label == "next");
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "exec" && edge.From == branchNode.Id && edge.To == translateNode.Id && edge.Label == "then");
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "data" && edge.From == speedNode.Id && edge.To == multiplyNode.Id);
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "data" &&
            edge.From == eventNode.Id &&
            edge.To == multiplyNode.Id &&
            edge.Label == "arg1" &&
            edge.FromPin == "delta" &&
            edge.ToPin == "arg1");
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "data" && edge.From == multiplyNode.Id && edge.To == vec3Node.Id);
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "data" && edge.From == vec3Node.Id && edge.To == translateNode.Id);
    }

    [Fact]
    public void Project_WhenGraphDebugWatchIsUsed_ReturnsWatchNodes()
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath("DebugWatch.ash.cs"));
        var graph = BlueprintGraphProjector.Project(module);

        var function = Assert.Single(graph.Functions);
        var amountWatch = FindNode(function, "Watch", "amount");
        var offsetWatch = FindNode(function, "Watch", "offset");

        Assert.Contains(function.Edges, edge =>
            edge.Kind == "data" && edge.To == amountWatch.Id && edge.Label == "value");
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "data" && edge.To == offsetWatch.Id && edge.Label == "value");
    }

    [Fact]
    public void Project_WhenIfElseIsUsed_ConnectsThenAndElseExecEdges()
    {
        var module = BehaviorIrLowerer.LowerText(
            BuildIfElseScript(),
            "IfElseMove.ash.cs");
        var graph = BlueprintGraphProjector.Project(module);

        var function = Assert.Single(graph.Functions);
        var branchNode = FindNode(function, "Branch", "Branch");
        var translateNodeIds = function.Nodes
            .Where(node => node.Kind == "Call" && node.Label == "asharia.transform.translate")
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(2, translateNodeIds.Count);
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "exec" &&
            edge.From == branchNode.Id &&
            edge.Label == "then" &&
            translateNodeIds.Contains(edge.To));
        Assert.Contains(function.Edges, edge =>
            edge.Kind == "exec" &&
            edge.From == branchNode.Id &&
            edge.Label == "else" &&
            translateNodeIds.Contains(edge.To));
    }

    [Fact]
    public void WriteJson_WhenGraphIsProjected_EmitsStableJsonShape()
    {
        var module = BehaviorIrLowerer.LowerFile(GetSamplePath("PlayerMove.ash.cs"));
        var graph = BlueprintGraphProjector.Project(module);
        using var writer = new StringWriter();

        BlueprintGraphReporter.WriteJson(graph, writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("com.game.PlayerMove", root.GetProperty("behaviorId").GetString());
        var function = root.GetProperty("functions").EnumerateArray().Single();
        Assert.Equal("Update", function.GetProperty("name").GetString());
        Assert.Contains(function.GetProperty("parameters").EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == "delta" &&
            parameter.GetProperty("type").GetString() == "float");
        Assert.Contains(function.GetProperty("nodes").EnumerateArray(), node =>
            node.GetProperty("kind").GetString() == "Branch" &&
            node.GetProperty("debugSiteId").GetString()!.StartsWith("ds_", StringComparison.Ordinal) &&
            node.GetProperty("breakabilityHint").GetString() == BehaviorIrBreakabilityHint.Breakable &&
            node.GetProperty("breakableVerified").GetBoolean() == false &&
            node.GetProperty("source").GetProperty("line").GetInt32() == 12);
        Assert.Contains(function.GetProperty("edges").EnumerateArray(), edge =>
            edge.GetProperty("kind").GetString() == "exec" &&
            edge.GetProperty("label").GetString() == "then");
        Assert.Contains(function.GetProperty("edges").EnumerateArray(), edge =>
            edge.GetProperty("kind").GetString() == "data" &&
            edge.GetProperty("fromPin").GetString() == "delta" &&
            edge.GetProperty("toPin").GetString() == "arg1");
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
