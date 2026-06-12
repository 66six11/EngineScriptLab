using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab.GraphCSharp;
using System.Globalization;

namespace ScriptLab;

public static class BehaviorIrLowerer
{
    public static BehaviorIrModule LowerFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var source = File.ReadAllText(fullPath);
        return LowerText(source, fullPath);
    }

    public static BehaviorIrModule LowerText(string source, string path)
    {
        var parseResult = GraphCSharpScriptParser.ParseText(source, path);
        if (parseResult.HasErrors)
        {
            throw new InvalidOperationException("Cannot lower a script with diagnostics.");
        }

        if (parseResult.Behavior is null)
        {
            throw new InvalidOperationException("Cannot lower a script without a behavior class.");
        }

        var tree = CSharpSyntaxTree.ParseText(source, path: Path.GetFullPath(path));
        var root = tree.GetCompilationUnitRoot();
        var semanticModel = GraphCSharpSemanticModelFactory.CreateSemanticModel(tree);
        var behaviorClass = FindBehaviorClass(root)
            ?? throw new InvalidOperationException("Cannot locate behavior class.");

        var module = new BehaviorIrModule(
            parseResult.Behavior.Id,
            LowerFields(behaviorClass, parseResult.Behavior, semanticModel),
            LowerFunctions(behaviorClass, parseResult.Behavior, semanticModel));
        BehaviorIrTypeVerifier.Verify(module);

        return AssignDebugSites(module);
    }

    private static BehaviorIrModule AssignDebugSites(BehaviorIrModule module)
    {
        var functions = module.Functions
            .Select(function =>
            {
                var usedDebugSiteIds = new HashSet<string>(StringComparer.Ordinal);
                var blocks = function.Blocks
                    .Select(block =>
                    {
                        var instructions = block.Instructions
                            .Select((instruction, index) => instruction with
                            {
                                DebugSiteId = CreateDebugSiteId(
                                    module.BehaviorId,
                                    function.Name,
                                    block.Name,
                                    index,
                                    instruction,
                                    usedDebugSiteIds),
                                BreakabilityHint = GetBreakabilityHint(instruction),
                                Observable = IsObservable(instruction)
                            })
                            .ToArray();
                        return block with { Instructions = instructions };
                    })
                    .ToArray();
                return function with { Blocks = blocks };
            })
            .ToArray();

        return module with { Functions = functions };
    }

    private static string CreateDebugSiteId(
        string behaviorId,
        string functionName,
        string blockName,
        int instructionIndex,
        BehaviorIrInstruction instruction,
        ISet<string> usedDebugSiteIds)
    {
        var baseKey = string.Join(
            "|",
            behaviorId,
            functionName,
            blockName,
            instructionIndex.ToString(CultureInfo.InvariantCulture),
            instruction.GetType().Name,
            instruction.Source.FileName,
            instruction.Source.Start.ToString(CultureInfo.InvariantCulture),
            instruction.Source.Length.ToString(CultureInfo.InvariantCulture),
            BehaviorIrText.Format(instruction));

        var suffix = 0;
        while (true)
        {
            var hashInput = suffix == 0
                ? baseKey
                : $"{baseKey}|{suffix.ToString(CultureInfo.InvariantCulture)}";
            var candidate = $"ds_{StablePositiveHash(hashInput):x8}";
            if (usedDebugSiteIds.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static string GetBreakabilityHint(BehaviorIrInstruction instruction)
    {
        return instruction switch
        {
            BehaviorIrBranch => BehaviorIrBreakabilityHint.Breakable,
            BehaviorIrCallFunction { Target: null } => BehaviorIrBreakabilityHint.Breakable,
            BehaviorIrAssign => BehaviorIrBreakabilityHint.Breakable,
            BehaviorIrStoreLocal => BehaviorIrBreakabilityHint.Breakable,
            BehaviorIrReturn { Source.Line: > 0 } => BehaviorIrBreakabilityHint.Breakable,
            BehaviorIrDebugWatch { IsStatement: true } => BehaviorIrBreakabilityHint.Breakable,
            BehaviorIrJump or BehaviorIrDeclareLocal or BehaviorIrReturn => BehaviorIrBreakabilityHint.SourceOnly,
            _ => BehaviorIrBreakabilityHint.Observable
        };
    }

    private static bool IsObservable(BehaviorIrInstruction instruction)
    {
        return instruction is not BehaviorIrJump and not BehaviorIrDeclareLocal;
    }

    private static int StablePositiveHash(string text)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;

        foreach (var character in text)
        {
            hash ^= character;
            hash *= prime;
        }

        return (int)(hash & 0x7fffffff);
    }

    private static IReadOnlyList<BehaviorIrField> LowerFields(
        ClassDeclarationSyntax behaviorClass,
        ScriptBehaviorSummary behavior,
        SemanticModel semanticModel)
    {
        var behaviorFieldNames = behavior.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        var behaviorFieldsByName = behavior.Fields.ToDictionary(field => field.Name, StringComparer.Ordinal);

        return behaviorClass.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables.Select(variable => new
            {
                Name = variable.Identifier.ValueText,
                Type = GraphCSharpTypeNameResolver.Resolve(
                    field.Declaration.Type,
                    semanticModel,
                    field.Declaration.Type.ToString()),
                InitialValue = variable.Initializer?.Value.ToString()
            }))
            .Where(field => behaviorFieldNames.Contains(field.Name))
            .Select(field =>
            {
                var summary = behaviorFieldsByName[field.Name];
                return new BehaviorIrField(
                    summary.FieldId ?? throw new InvalidOperationException(
                        $"Cannot lower field '{field.Name}' without a stable field id."),
                    field.Name,
                    field.Type,
                    field.InitialValue);
            })
            .ToArray();
    }

    private static IReadOnlyList<BehaviorIrFunction> LowerFunctions(
        ClassDeclarationSyntax behaviorClass,
        ScriptBehaviorSummary behavior,
        SemanticModel semanticModel)
    {
        var fieldIdsByName = behavior.Fields
            .Where(field => field.FieldId is not null)
            .ToDictionary(field => field.Name, field => field.FieldId!.Value, StringComparer.Ordinal);

        return behaviorClass.Members
            .OfType<MethodDeclarationSyntax>()
            .Select(method => LowerFunction(method, fieldIdsByName, semanticModel))
            .ToArray();
    }

    private static BehaviorIrFunction LowerFunction(
        MethodDeclarationSyntax method,
        IReadOnlyDictionary<string, FieldId> fieldIdsByName,
        SemanticModel semanticModel)
    {
        var builder = new FunctionBuilder(fieldIdsByName, semanticModel, GetSourceSpan(method));

        if (method.Body is not null)
        {
            foreach (var statement in method.Body.Statements)
            {
                builder.LowerStatement(statement);
            }
        }

        builder.EnsureCurrentBlockReturns();

        return new BehaviorIrFunction(
            method.Identifier.ValueText,
            method.ParameterList.Parameters
                .Select(parameter => new BehaviorIrParameter(
                    parameter.Identifier.ValueText,
                    GraphCSharpTypeNameResolver.Resolve(
                        parameter.Type,
                        semanticModel,
                        parameter.Type?.ToString() ?? "var")))
                .ToArray(),
            builder.Blocks);
    }

    private static ClassDeclarationSyntax? FindBehaviorClass(CompilationUnitSyntax root)
    {
        return root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(classDeclaration =>
                classDeclaration.BaseList?.Types.Any(type => type.Type.ToString() == "BehaviorComponent") == true);
    }

    private sealed class FunctionBuilder
    {
        private readonly IReadOnlyDictionary<string, FieldId> fieldIdsByName;
        private readonly SemanticModel semanticModel;
        private readonly BehaviorSourceSpan defaultSource;
        private readonly List<MutableBlock> blocks = new();
        private int tempIndex;
        private int blockIndex;
        private MutableBlock currentBlock;

        public FunctionBuilder(
            IReadOnlyDictionary<string, FieldId> fieldIdsByName,
            SemanticModel semanticModel,
            BehaviorSourceSpan defaultSource)
        {
            this.fieldIdsByName = fieldIdsByName;
            this.semanticModel = semanticModel;
            this.defaultSource = defaultSource;
            currentBlock = CreateBlock("entry");
        }

        public IReadOnlyList<BehaviorIrBlock> Blocks => blocks
            .Select(block => new BehaviorIrBlock(block.Name, block.Instructions.ToArray()))
            .ToArray();

        public void LowerStatement(StatementSyntax statement)
        {
            switch (statement)
            {
                case BlockSyntax block:
                    foreach (var nestedStatement in block.Statements)
                    {
                        LowerStatement(nestedStatement);
                    }

                    break;

                case IfStatementSyntax ifStatement:
                    LowerIfStatement(ifStatement);
                    break;

                case LocalDeclarationStatementSyntax localDeclaration:
                    LowerLocalDeclaration(localDeclaration);
                    break;

                case ExpressionStatementSyntax expressionStatement:
                    LowerExpressionStatement(expressionStatement.Expression);
                    break;

                case ReturnStatementSyntax:
                    currentBlock.Instructions.Add(new BehaviorIrReturn(GetSourceSpan(statement)));
                    break;

                default:
                    throw CreateUnsupportedLoweringException(statement);
            }
        }

        public void EnsureCurrentBlockReturns()
        {
            if (currentBlock.Instructions.Count == 0 || currentBlock.Instructions[^1] is not BehaviorIrReturn)
            {
                currentBlock.Instructions.Add(new BehaviorIrReturn(defaultSource));
            }
        }

        private void LowerIfStatement(IfStatementSyntax ifStatement)
        {
            var condition = LowerExpression(ifStatement.Condition);
            var thenBlock = CreateBlock($"then_{blockIndex++}");
            var elseClause = ifStatement.Else;
            var elseBlock = elseClause is null
                ? null
                : CreateBlock($"else_{blockIndex++}");
            var exitBlock = CreateBlock($"exit_{blockIndex++}");

            var source = GetSourceSpan(ifStatement);
            currentBlock.Instructions.Add(new BehaviorIrBranch(
                condition,
                thenBlock.Name,
                elseBlock?.Name ?? exitBlock.Name,
                source));

            currentBlock = thenBlock;
            LowerStatement(ifStatement.Statement);
            currentBlock.Instructions.Add(new BehaviorIrJump(exitBlock.Name, source));

            if (elseClause is not null && elseBlock is not null)
            {
                currentBlock = elseBlock;
                LowerStatement(elseClause.Statement);
                currentBlock.Instructions.Add(new BehaviorIrJump(exitBlock.Name, source));
            }

            currentBlock = exitBlock;
        }

        private void LowerLocalDeclaration(LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                var localType = GraphCSharpTypeNameResolver.ResolveLocal(
                    variable,
                    localDeclaration.Declaration.Type,
                    semanticModel);
                currentBlock.Instructions.Add(new BehaviorIrDeclareLocal(
                    variable.Identifier.ValueText,
                    localType,
                    GetSourceSpan(localDeclaration)));

                if (variable.Initializer is null)
                {
                    continue;
                }

                var value = LowerExpression(variable.Initializer.Value);
                currentBlock.Instructions.Add(new BehaviorIrStoreLocal(
                    variable.Identifier.ValueText,
                    value,
                    GetSourceSpan(localDeclaration)));
            }
        }

        private void LowerExpressionStatement(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case InvocationExpressionSyntax invocation:
                    LowerInvocation(invocation, emitResult: false);
                    break;

                case AssignmentExpressionSyntax assignment:
                    var value = LowerExpression(assignment.Right);
                    currentBlock.Instructions.Add(new BehaviorIrAssign(
                        assignment.Left.ToString(),
                        value,
                        GetSourceSpan(assignment)));
                    break;

                default:
                    _ = LowerExpression(expression);
                    break;
            }
        }

        private string LowerExpression(ExpressionSyntax expression)
        {
            return expression switch
            {
                LiteralExpressionSyntax literal => EmitValue(target =>
                    new BehaviorIrLoadConst(
                        target,
                        GetExpressionType(literal, "var"),
                        literal.Token.Text,
                        GetSourceSpan(literal))),
                IdentifierNameSyntax identifier => LowerIdentifier(identifier),
                MemberAccessExpressionSyntax memberAccess => LowerMemberAccess(memberAccess),
                BinaryExpressionSyntax binary => LowerBinary(binary),
                ObjectCreationExpressionSyntax objectCreation => LowerObjectCreation(objectCreation),
                InvocationExpressionSyntax invocation => LowerInvocation(invocation, emitResult: true),
                ParenthesizedExpressionSyntax parenthesized => LowerExpression(parenthesized.Expression),
                _ => throw CreateUnsupportedLoweringException(expression)
            };
        }

        private string LowerIdentifier(IdentifierNameSyntax identifier)
        {
            var name = identifier.Identifier.ValueText;

            if (name == "Self")
            {
                return EmitValue(target => new BehaviorIrLoadSelf(
                    target,
                    GetExpressionType(identifier, "EntityRef"),
                    GetSourceSpan(identifier)));
            }

            if (fieldIdsByName.TryGetValue(name, out var fieldId))
            {
                return EmitValue(target => new BehaviorIrLoadField(
                    target,
                    GetExpressionType(identifier, "var"),
                    fieldId,
                    name,
                    GetSourceSpan(identifier)));
            }

            return EmitValue(target => new BehaviorIrLoadLocal(
                target,
                GetExpressionType(identifier, "var"),
                name,
                GetSourceSpan(identifier)));
        }

        private string LowerMemberAccess(MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Expression is IdentifierNameSyntax typeName && typeName.Identifier.ValueText == "Key")
            {
                return EmitValue(target => new BehaviorIrLoadEnum(
                    target,
                    GetExpressionType(memberAccess, "Key"),
                    memberAccess.ToString(),
                    GetSourceSpan(memberAccess)));
            }

            return EmitValue(target => new BehaviorIrLoadMember(
                target,
                GetExpressionType(memberAccess, "var"),
                memberAccess.ToString(),
                GetSourceSpan(memberAccess)));
        }

        private string LowerBinary(BinaryExpressionSyntax binary)
        {
            var left = LowerExpression(binary.Left);
            var right = LowerExpression(binary.Right);
            return EmitValue(target => new BehaviorIrBinaryOp(
                target,
                GetExpressionType(binary, "var"),
                GetBinaryOperator(binary),
                left,
                right,
                GetSourceSpan(binary)));
        }

        private string LowerObjectCreation(ObjectCreationExpressionSyntax objectCreation)
        {
            var arguments = objectCreation.ArgumentList?.Arguments
                .Select(argument => LowerExpression(argument.Expression))
                .ToArray()
                ?? Array.Empty<string>();

            return EmitValue(target => new BehaviorIrMakeStruct(
                target,
                GetObjectCreationTypeName(objectCreation),
                arguments,
                GetSourceSpan(objectCreation)));
        }

        private string GetExpressionType(ExpressionSyntax expression, string fallback)
        {
            return GraphCSharpTypeNameResolver.Resolve(
                semanticModel.GetTypeInfo(expression).Type,
                fallback);
        }

        private string GetObjectCreationTypeName(ObjectCreationExpressionSyntax objectCreation)
        {
            var type = semanticModel.GetTypeInfo(objectCreation).Type;
            return type is not null &&
                GraphCSharpRuleSet.TryGetConstructibleValueTypeName(
                    type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    requireQualifiedMatch: true,
                    out var canonicalTypeName)
                ? canonicalTypeName
                : objectCreation.Type.ToString();
        }

        private string LowerInvocation(InvocationExpressionSyntax invocation, bool emitResult)
        {
            if (TryLowerGraphDebugInvocation(invocation, emitResult, out var debugValue))
            {
                return debugValue;
            }

            var arguments = invocation.ArgumentList.Arguments
                .Select(argument => LowerExpression(argument.Expression))
                .ToArray();
            var functionName = invocation.Expression.ToString();
            var hasBinding = GraphCSharpBindingRegistry.TryResolveFunctionBinding(
                invocation,
                semanticModel,
                out var binding,
                out var resolvedFunctionName);
            var functionId = hasBinding
                ? binding.FunctionId
                : new FunctionId(resolvedFunctionName.Length == 0 ? functionName : resolvedFunctionName);

            if (emitResult)
            {
                return EmitValue(target => new BehaviorIrCallFunction(
                    target,
                    hasBinding ? GetInvocationReturnType(invocation, binding) : GetExpressionType(invocation, "var"),
                    functionId,
                    arguments,
                    GetSourceSpan(invocation)));
            }

            currentBlock.Instructions.Add(new BehaviorIrCallFunction(
                null,
                null,
                functionId,
                arguments,
                GetSourceSpan(invocation)));
            return string.Empty;
        }

        private string? GetInvocationReturnType(
            InvocationExpressionSyntax invocation,
            GraphCSharpFunctionBinding binding)
        {
            if (binding.ReturnType is null)
            {
                return null;
            }

            return binding.ReturnType == "*"
                ? GetExpressionType(invocation, "var")
                : binding.ReturnType;
        }

        private bool TryLowerGraphDebugInvocation(
            InvocationExpressionSyntax invocation,
            bool emitResult,
            out string value)
        {
            value = string.Empty;

            if (!GraphCSharpBindingRegistry.TryResolveFunctionBinding(
                    invocation,
                    semanticModel,
                    out var binding,
                    out _) ||
                binding.FunctionId.Value is not ("asharia.debug.inspect" or "asharia.debug.watch"))
            {
                return false;
            }

            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count < 2)
            {
                throw CreateUnsupportedLoweringException(invocation);
            }

            var watchName = GetDebugWatchName(arguments[0].Expression);
            value = LowerExpression(arguments[1].Expression);
            currentBlock.Instructions.Add(new BehaviorIrDebugWatch(
                watchName,
                value,
                binding.FunctionId.Value == "asharia.debug.watch" && !emitResult,
                GetSourceSpan(invocation)));

            return true;
        }

        private static string GetDebugWatchName(ExpressionSyntax expression)
        {
            return expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression)
                    ? literal.Token.ValueText
                    : expression.ToString();
        }

        private string EmitValue(Func<string, BehaviorIrInstruction> createInstruction)
        {
            var temp = NextTemp();
            currentBlock.Instructions.Add(createInstruction(temp));
            return temp;
        }

        private string NextTemp()
        {
            return $"%{tempIndex++}";
        }

        private MutableBlock CreateBlock(string name)
        {
            var block = new MutableBlock(name);
            blocks.Add(block);
            return block;
        }

        private static string GetBinaryOperator(BinaryExpressionSyntax binary)
        {
            return binary.Kind() switch
            {
                SyntaxKind.MultiplyExpression => "Multiply",
                SyntaxKind.AddExpression => "Add",
                SyntaxKind.SubtractExpression => "Subtract",
                SyntaxKind.DivideExpression => "Divide",
                SyntaxKind.ModuloExpression => "Modulo",
                SyntaxKind.EqualsExpression => "Equals",
                SyntaxKind.NotEqualsExpression => "NotEquals",
                SyntaxKind.LessThanExpression => "LessThan",
                SyntaxKind.LessThanOrEqualExpression => "LessThanOrEqual",
                SyntaxKind.GreaterThanExpression => "GreaterThan",
                SyntaxKind.GreaterThanOrEqualExpression => "GreaterThanOrEqual",
                _ => binary.Kind().ToString()
            };
        }

        private static InvalidOperationException CreateUnsupportedLoweringException(SyntaxNode node)
        {
            return new InvalidOperationException(
                $"Graph C# analyzer allowed unsupported syntax '{node.Kind()}' to reach IR lowering.");
        }
    }

    private static BehaviorSourceSpan GetSourceSpan(SyntaxNode node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        var start = lineSpan.StartLinePosition;
        var sourceSpan = node.Span;

        return new BehaviorSourceSpan(
            Path.GetFileName(lineSpan.Path),
            start.Line + 1,
            start.Character + 1,
            sourceSpan.Start,
            sourceSpan.Length);
    }

    private sealed class MutableBlock
    {
        public MutableBlock(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public List<BehaviorIrInstruction> Instructions { get; } = new();
    }
}
