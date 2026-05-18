using System.Text.Json;

namespace ScriptLab;

public sealed record BlueprintGraphModule(
    string BehaviorId,
    IReadOnlyList<BlueprintGraphFunction> Functions);

public sealed record BlueprintGraphFunction(
    string Name,
    IReadOnlyList<BlueprintGraphParameter> Parameters,
    IReadOnlyList<BlueprintGraphNode> Nodes,
    IReadOnlyList<BlueprintGraphEdge> Edges);

public sealed record BlueprintGraphParameter(string Name, string Type);

public sealed record BlueprintGraphNode(
    string Id,
    string DebugSiteId,
    string Kind,
    string Label,
    BehaviorSourceSpan Source,
    string BreakabilityHint,
    bool BreakableVerified,
    bool Observable,
    string? OwningBreakableDebugSiteId);

public sealed record BlueprintGraphEdge(
    string Id,
    string From,
    string To,
    string Kind,
    string Label,
    string FromPin,
    string ToPin);

public static class BlueprintGraphProjector
{
    public static BlueprintGraphModule Project(BehaviorIrModule module)
    {
        return new BlueprintGraphModule(
            module.BehaviorId,
            module.Functions.Select(ProjectFunction).ToArray());
    }

    private static BlueprintGraphFunction ProjectFunction(BehaviorIrFunction function)
    {
        var builder = new FunctionGraphBuilder(function.Name, function.Parameters);

        foreach (var block in function.Blocks)
        {
            builder.ProjectBlock(block);
        }

        builder.ConnectControlFlow();
        return builder.Build();
    }

    private sealed class FunctionGraphBuilder
    {
        private readonly string functionName;
        private readonly IReadOnlyList<BlueprintGraphParameter> parameters;
        private readonly HashSet<string> parameterNames;
        private readonly List<BlueprintGraphNode> nodes = new();
        private readonly List<BlueprintGraphEdge> edges = new();
        private readonly Dictionary<string, TempSource> tempSources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> blockControlNodes = new(StringComparer.Ordinal);
        private readonly List<BranchProjection> branches = new();
        private readonly List<JumpProjection> jumps = new();
        private readonly string eventNodeId;
        private int nextNodeId;
        private int nextEdgeId;

        public FunctionGraphBuilder(string functionName, IReadOnlyList<BehaviorIrParameter> parameters)
        {
            this.functionName = functionName;
            this.parameters = parameters
                .Select(parameter => new BlueprintGraphParameter(parameter.Name, parameter.Type))
                .ToArray();
            parameterNames = parameters
                .Select(parameter => parameter.Name)
                .ToHashSet(StringComparer.Ordinal);
            var eventNode = CreateGeneratedNode("Event", functionName, BehaviorSourceSpan.Generated);
            eventNodeId = eventNode.Id;
            blockControlNodes["entry"] = new List<string> { eventNode.Id };
        }

        public void ProjectBlock(BehaviorIrBlock block)
        {
            if (!blockControlNodes.TryGetValue(block.Name, out var controlNodes))
            {
                controlNodes = new List<string>();
                blockControlNodes.Add(block.Name, controlNodes);
            }

            foreach (var instruction in block.Instructions)
            {
                ProjectInstruction(block.Name, controlNodes, instruction);
            }
        }

        public void ConnectControlFlow()
        {
            foreach (var controlNodes in blockControlNodes.Values)
            {
                for (var index = 0; index < controlNodes.Count - 1; index++)
                {
                    AddEdge(controlNodes[index], controlNodes[index + 1], "exec", "next");
                }
            }

            foreach (var branch in branches)
            {
                ConnectToBlock(branch.NodeId, branch.ThenBlock, "exec", "then");
                ConnectToBlock(branch.NodeId, branch.ElseBlock, "exec", "else");
            }

            foreach (var jump in jumps)
            {
                var source = blockControlNodes.TryGetValue(jump.FromBlock, out var controlNodes)
                    ? controlNodes.LastOrDefault()
                    : null;

                if (source is not null)
                {
                    ConnectToBlock(source, jump.TargetBlock, "exec", "jump");
                }
            }
        }

        public BlueprintGraphFunction Build()
        {
            PropagateOwningBreakableSites();
            return new BlueprintGraphFunction(functionName, parameters, nodes.ToArray(), edges.ToArray());
        }

        private void ProjectInstruction(
            string blockName,
            List<string> controlNodes,
            BehaviorIrInstruction instruction)
        {
            switch (instruction)
            {
                case BehaviorIrCallFunction { Target: not null } valueCall:
                    var valueCallNode = CreateCallNode(valueCall);
                    tempSources[valueCall.Target] = new TempSource(valueCallNode.Id, "value");
                    AddDataEdges(valueCall.Arguments, valueCallNode.Id);
                    break;

                case BehaviorIrValueInstruction valueInstruction:
                    if (TryProjectParameterLoad(valueInstruction))
                    {
                        break;
                    }

                    var valueNode = CreateValueNode(valueInstruction);
                    tempSources[valueInstruction.Target] = new TempSource(valueNode.Id, "value");
                    AddDataEdges(GetArguments(valueInstruction), valueNode.Id);
                    break;

                case BehaviorIrCallFunction { Target: null } call:
                    var callNode = CreateCallNode(call);
                    AddDataEdges(call.Arguments, callNode.Id);
                    controlNodes.Add(callNode.Id);
                    break;

                case BehaviorIrBranch branch:
                    var branchNode = CreateNode("Branch", "Branch", branch);
                    AddTempEdge(branch.Condition, branchNode.Id, "condition");
                    controlNodes.Add(branchNode.Id);
                    branches.Add(new BranchProjection(branchNode.Id, branch.ThenBlock, branch.ElseBlock));
                    break;

                case BehaviorIrDebugWatch debugWatch:
                    var watchNode = CreateNode("Watch", debugWatch.Name, debugWatch);
                    AddTempEdge(debugWatch.Value, watchNode.Id, "value");
                    if (debugWatch.IsStatement)
                    {
                        controlNodes.Add(watchNode.Id);
                    }

                    break;

                case BehaviorIrJump jump:
                    jumps.Add(new JumpProjection(blockName, jump.TargetBlock));
                    break;

                case BehaviorIrReturn returnInstruction:
                    controlNodes.Add(CreateNode("Return", "Return", returnInstruction).Id);
                    break;
            }
        }

        private BlueprintGraphNode CreateValueNode(BehaviorIrValueInstruction instruction)
        {
            return instruction switch
            {
                BehaviorIrLoadEnum loadEnum => CreateNode("Enum", loadEnum.Value, loadEnum),
                BehaviorIrLoadConst loadConst => CreateNode("Const", loadConst.Value, loadConst),
                BehaviorIrLoadField loadField => CreateNode("GetField", loadField.FieldName, loadField),
                BehaviorIrLoadLocal loadLocal => CreateNode("GetLocal", loadLocal.LocalName, loadLocal),
                BehaviorIrLoadSelf loadSelf => CreateNode("Self", "Self", loadSelf),
                BehaviorIrLoadMember loadMember => CreateNode("GetMember", loadMember.Member, loadMember),
                BehaviorIrBinaryOp binaryOp => CreateNode("BinaryOp", binaryOp.Operator, binaryOp),
                BehaviorIrMakeStruct makeStruct => CreateNode("MakeStruct", makeStruct.Type, makeStruct),
                BehaviorIrUnsupportedExpression unsupported => CreateNode("Value", unsupported.Expression, unsupported),
                _ => CreateNode("Value", BehaviorIrText.Format(instruction), instruction)
            };
        }

        private BlueprintGraphNode CreateCallNode(BehaviorIrCallFunction call)
        {
            return CreateNode("Call", call.FunctionId, call);
        }

        private bool TryProjectParameterLoad(BehaviorIrValueInstruction instruction)
        {
            if (instruction is not BehaviorIrLoadLocal loadLocal ||
                !parameterNames.Contains(loadLocal.LocalName))
            {
                return false;
            }

            tempSources[loadLocal.Target] = new TempSource(eventNodeId, loadLocal.LocalName);
            return true;
        }

        private BlueprintGraphNode CreateGeneratedNode(string kind, string label, BehaviorSourceSpan source)
        {
            var node = new BlueprintGraphNode(
                $"n{nextNodeId++}",
                $"generated:{functionName}:{kind}:{nextNodeId}",
                kind,
                label,
                source,
                BehaviorIrBreakabilityHint.SourceOnly,
                BreakableVerified: false,
                Observable: false,
                OwningBreakableDebugSiteId: null);
            nodes.Add(node);
            return node;
        }

        private BlueprintGraphNode CreateNode(string kind, string label, BehaviorIrInstruction instruction)
        {
            var owningBreakableDebugSiteId = instruction.BreakabilityHint == BehaviorIrBreakabilityHint.Breakable
                ? instruction.DebugSiteId
                : null;
            var node = new BlueprintGraphNode(
                $"n{nextNodeId++}",
                instruction.DebugSiteId,
                kind,
                label,
                instruction.Source,
                instruction.BreakabilityHint,
                BreakableVerified: false,
                Observable: instruction.Observable,
                OwningBreakableDebugSiteId: owningBreakableDebugSiteId);
            nodes.Add(node);
            return node;
        }

        private void PropagateOwningBreakableSites()
        {
            foreach (var node in nodes.Where(node => node.BreakabilityHint == BehaviorIrBreakabilityHint.Breakable).ToArray())
            {
                AssignOwnerToInputs(node.Id, node.DebugSiteId, new HashSet<string>(StringComparer.Ordinal));
            }
        }

        private void AssignOwnerToInputs(string nodeId, string ownerDebugSiteId, ISet<string> visitedNodeIds)
        {
            if (!visitedNodeIds.Add(nodeId))
            {
                return;
            }

            foreach (var edge in edges.Where(edge => edge.Kind == "data" && edge.To == nodeId).ToArray())
            {
                var sourceIndex = nodes.FindIndex(node => node.Id == edge.From);
                if (sourceIndex < 0)
                {
                    continue;
                }

                var sourceNode = nodes[sourceIndex];
                if (sourceNode.Kind == "Event" ||
                    sourceNode.BreakabilityHint == BehaviorIrBreakabilityHint.Breakable)
                {
                    continue;
                }

                if (sourceNode.OwningBreakableDebugSiteId is null)
                {
                    nodes[sourceIndex] = sourceNode with
                    {
                        OwningBreakableDebugSiteId = ownerDebugSiteId
                    };
                }

                AssignOwnerToInputs(sourceNode.Id, ownerDebugSiteId, visitedNodeIds);
            }
        }

        private void AddDataEdges(IEnumerable<string> temps, string nodeId)
        {
            var index = 0;
            foreach (var temp in temps)
            {
                AddTempEdge(temp, nodeId, $"arg{index++}");
            }
        }

        private void AddTempEdge(string temp, string nodeId, string label)
        {
            if (tempSources.TryGetValue(temp, out var source))
            {
                AddEdge(source.NodeId, nodeId, "data", label, source.PinId, label);
            }
        }

        private void ConnectToBlock(string fromNode, string blockName, string kind, string label)
        {
            if (blockControlNodes.TryGetValue(blockName, out var controlNodes) &&
                controlNodes.FirstOrDefault() is { } targetNode)
            {
                AddEdge(fromNode, targetNode, kind, label);
            }
        }

        private void AddEdge(
            string from,
            string to,
            string kind,
            string label,
            string? fromPin = null,
            string? toPin = null)
        {
            edges.Add(new BlueprintGraphEdge(
                $"e{nextEdgeId++}",
                from,
                to,
                kind,
                label,
                fromPin ?? label,
                toPin ?? label));
        }

        private static IReadOnlyList<string> GetArguments(BehaviorIrValueInstruction instruction)
        {
            return instruction switch
            {
                BehaviorIrBinaryOp binaryOp => new[] { binaryOp.Left, binaryOp.Right },
                BehaviorIrMakeStruct makeStruct => makeStruct.Arguments,
                _ => Array.Empty<string>()
            };
        }
    }

    private sealed record BranchProjection(string NodeId, string ThenBlock, string ElseBlock);

    private sealed record JumpProjection(string FromBlock, string TargetBlock);

    private sealed record TempSource(string NodeId, string PinId);
}

public static class BlueprintGraphReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void Write(BlueprintGraphModule module, TextWriter writer)
    {
        writer.WriteLine($"BlueprintGraph {module.BehaviorId}");

        foreach (var function in module.Functions)
        {
            writer.WriteLine($"Function {function.Name}");
            writer.WriteLine("Nodes:");

            foreach (var node in function.Nodes)
            {
                var owner = node.OwningBreakableDebugSiteId is null
                    ? string.Empty
                    : $" owner={node.OwningBreakableDebugSiteId}";
                writer.WriteLine(
                    $"  {node.Id} {node.DebugSiteId} {node.Kind} {node.Label} [{node.BreakabilityHint}]{owner} @ {BehaviorIrText.FormatSource(node.Source)}");
            }

            writer.WriteLine("Edges:");

            foreach (var edge in function.Edges)
            {
                writer.WriteLine($"  {edge.Id} {edge.Kind} {edge.From} -> {edge.To} [{edge.Label}]");
            }
        }
    }

    public static void WriteJson(BlueprintGraphModule module, TextWriter writer)
    {
        writer.WriteLine(JsonSerializer.Serialize(
            new BlueprintGraphJsonDocument(2, module.BehaviorId, module.Functions),
            JsonOptions));
    }
}

public sealed record BlueprintGraphJsonDocument(
    int SchemaVersion,
    string BehaviorId,
    IReadOnlyList<BlueprintGraphFunction> Functions);
