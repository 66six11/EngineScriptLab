using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab.GraphCSharp;
using System.Globalization;

namespace ScriptLab;

public sealed record BehaviorIrModule(
    string BehaviorId,
    IReadOnlyList<BehaviorIrField> Fields,
    IReadOnlyList<BehaviorIrFunction> Functions);

public sealed record BehaviorIrField(string Name, string Type, string? InitialValue);

public sealed record BehaviorIrFunction(
    string Name,
    IReadOnlyList<BehaviorIrParameter> Parameters,
    IReadOnlyList<BehaviorIrBlock> Blocks);

public sealed record BehaviorIrParameter(string Name, string Type);

public sealed record BehaviorIrBlock(string Name, IReadOnlyList<BehaviorIrInstruction> Instructions);

public sealed record BehaviorSourceSpan(string FileName, int Line, int Column, int Start, int Length)
{
    public static BehaviorSourceSpan Generated { get; } = new("<generated>", 0, 0, 0, 0);
}

public static class BehaviorIrBreakabilityHint
{
    public const string Breakable = "breakable";
    public const string Observable = "observable";
    public const string SourceOnly = "sourceOnly";
}

public abstract record BehaviorIrInstruction(BehaviorSourceSpan Source)
{
    public string DebugSiteId { get; init; } = string.Empty;

    public string BreakabilityHint { get; init; } = BehaviorIrBreakabilityHint.Observable;

    public bool Observable { get; init; } = true;
}

public abstract record BehaviorIrValueInstruction(string Target, BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrLoadConst(string Target, string Value, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadEnum(string Target, string Value, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadField(string Target, string FieldName, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadLocal(string Target, string LocalName, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadSelf(string Target, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrLoadMember(string Target, string Member, BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrBinaryOp(
    string Target,
    string Operator,
    string Left,
    string Right,
    BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrMakeStruct(
    string Target,
    string Type,
    IReadOnlyList<string> Arguments,
    BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

public sealed record BehaviorIrCallFunction(
    string? Target,
    string FunctionId,
    IReadOnlyList<string> Arguments,
    BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrBranch(
    string Condition,
    string ThenBlock,
    string ElseBlock,
    BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrJump(string TargetBlock, BehaviorSourceSpan Source) : BehaviorIrInstruction(Source);

public sealed record BehaviorIrReturn(BehaviorSourceSpan Source) : BehaviorIrInstruction(Source);

public sealed record BehaviorIrDeclareLocal(string LocalName, BehaviorSourceSpan Source) : BehaviorIrInstruction(Source);

public sealed record BehaviorIrStoreLocal(string LocalName, string Value, BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrAssign(string TargetExpression, string Value, BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrDebugWatch(
    string Name,
    string Value,
    bool IsStatement,
    BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrUnsupportedStatement(string Kind, BehaviorSourceSpan Source)
    : BehaviorIrInstruction(Source);

public sealed record BehaviorIrUnsupportedExpression(
    string Target,
    string Expression,
    BehaviorSourceSpan Source)
    : BehaviorIrValueInstruction(Target, Source);

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
        var behaviorClass = FindBehaviorClass(root)
            ?? throw new InvalidOperationException("Cannot locate behavior class.");

        return AssignDebugSites(new BehaviorIrModule(
            parseResult.Behavior.Id,
            LowerFields(behaviorClass, parseResult.Behavior),
            LowerFunctions(behaviorClass, parseResult.Behavior)));
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
        ScriptBehaviorSummary behavior)
    {
        var behaviorFieldNames = behavior.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

        return behaviorClass.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables.Select(variable => new
            {
                Name = variable.Identifier.ValueText,
                Type = field.Declaration.Type.ToString(),
                InitialValue = variable.Initializer?.Value.ToString()
            }))
            .Where(field => behaviorFieldNames.Contains(field.Name))
            .Select(field => new BehaviorIrField(field.Name, field.Type, field.InitialValue))
            .ToArray();
    }

    private static IReadOnlyList<BehaviorIrFunction> LowerFunctions(
        ClassDeclarationSyntax behaviorClass,
        ScriptBehaviorSummary behavior)
    {
        var fieldNames = behavior.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

        return behaviorClass.Members
            .OfType<MethodDeclarationSyntax>()
            .Select(method => LowerFunction(method, fieldNames))
            .ToArray();
    }

    private static BehaviorIrFunction LowerFunction(MethodDeclarationSyntax method, ISet<string> fieldNames)
    {
        var builder = new FunctionBuilder(fieldNames, GetSourceSpan(method));

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
                    parameter.Type?.ToString() ?? "var"))
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
        private readonly ISet<string> fieldNames;
        private readonly BehaviorSourceSpan defaultSource;
        private readonly List<MutableBlock> blocks = new();
        private int tempIndex;
        private int blockIndex;
        private MutableBlock currentBlock;

        public FunctionBuilder(ISet<string> fieldNames, BehaviorSourceSpan defaultSource)
        {
            this.fieldNames = fieldNames;
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
                    currentBlock.Instructions.Add(new BehaviorIrUnsupportedStatement(
                        statement.Kind().ToString(),
                        GetSourceSpan(statement)));
                    break;
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
                if (variable.Initializer is null)
                {
                    currentBlock.Instructions.Add(new BehaviorIrDeclareLocal(
                        variable.Identifier.ValueText,
                        GetSourceSpan(localDeclaration)));
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
                    new BehaviorIrLoadConst(target, literal.Token.Text, GetSourceSpan(literal))),
                IdentifierNameSyntax identifier => LowerIdentifier(identifier),
                MemberAccessExpressionSyntax memberAccess => LowerMemberAccess(memberAccess),
                BinaryExpressionSyntax binary => LowerBinary(binary),
                ObjectCreationExpressionSyntax objectCreation => LowerObjectCreation(objectCreation),
                InvocationExpressionSyntax invocation => LowerInvocation(invocation, emitResult: true),
                ParenthesizedExpressionSyntax parenthesized => LowerExpression(parenthesized.Expression),
                _ => EmitValue(target => new BehaviorIrUnsupportedExpression(
                    target,
                    expression.ToString(),
                    GetSourceSpan(expression)))
            };
        }

        private string LowerIdentifier(IdentifierNameSyntax identifier)
        {
            var name = identifier.Identifier.ValueText;

            if (name == "Self")
            {
                return EmitValue(target => new BehaviorIrLoadSelf(target, GetSourceSpan(identifier)));
            }

            if (fieldNames.Contains(name))
            {
                return EmitValue(target => new BehaviorIrLoadField(target, name, GetSourceSpan(identifier)));
            }

            return EmitValue(target => new BehaviorIrLoadLocal(target, name, GetSourceSpan(identifier)));
        }

        private string LowerMemberAccess(MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Expression is IdentifierNameSyntax typeName && typeName.Identifier.ValueText == "Key")
            {
                return EmitValue(target => new BehaviorIrLoadEnum(
                    target,
                    memberAccess.ToString(),
                    GetSourceSpan(memberAccess)));
            }

            return EmitValue(target => new BehaviorIrLoadMember(
                target,
                memberAccess.ToString(),
                GetSourceSpan(memberAccess)));
        }

        private string LowerBinary(BinaryExpressionSyntax binary)
        {
            var left = LowerExpression(binary.Left);
            var right = LowerExpression(binary.Right);
            return EmitValue(target => new BehaviorIrBinaryOp(
                target,
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
                objectCreation.Type.ToString(),
                arguments,
                GetSourceSpan(objectCreation)));
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
            if (!GraphCSharpBindingRegistry.TryGetFunctionId(functionName, out var functionId))
            {
                functionId = functionName;
            }

            if (emitResult)
            {
                return EmitValue(target => new BehaviorIrCallFunction(
                    target,
                    functionId,
                    arguments,
                    GetSourceSpan(invocation)));
            }

            currentBlock.Instructions.Add(new BehaviorIrCallFunction(
                null,
                functionId,
                arguments,
                GetSourceSpan(invocation)));
            return string.Empty;
        }

        private bool TryLowerGraphDebugInvocation(
            InvocationExpressionSyntax invocation,
            bool emitResult,
            out string value)
        {
            value = string.Empty;

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Expression.ToString() != "GraphDebug")
            {
                return false;
            }

            var methodName = memberAccess.Name.Identifier.ValueText;
            if (methodName is not ("Inspect" or "Watch"))
            {
                return false;
            }

            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count < 2)
            {
                currentBlock.Instructions.Add(new BehaviorIrUnsupportedStatement(
                    invocation.ToString(),
                    GetSourceSpan(invocation)));
                return true;
            }

            var watchName = GetDebugWatchName(arguments[0].Expression);
            value = LowerExpression(arguments[1].Expression);
            currentBlock.Instructions.Add(new BehaviorIrDebugWatch(
                watchName,
                value,
                methodName == "Watch" && !emitResult,
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

public static class BehaviorIrText
{
    public static string FormatWithSource(BehaviorIrInstruction instruction)
    {
        return $"{Format(instruction)} @ {FormatSource(instruction.Source)}";
    }

    public static string Format(BehaviorIrInstruction instruction)
    {
        return instruction switch
        {
            BehaviorIrLoadConst loadConst => $"{loadConst.Target} = LoadConst {loadConst.Value}",
            BehaviorIrLoadEnum loadEnum => $"{loadEnum.Target} = LoadEnum {loadEnum.Value}",
            BehaviorIrLoadField loadField => $"{loadField.Target} = LoadField {loadField.FieldName}",
            BehaviorIrLoadLocal loadLocal => $"{loadLocal.Target} = LoadLocal {loadLocal.LocalName}",
            BehaviorIrLoadSelf loadSelf => $"{loadSelf.Target} = LoadSelf",
            BehaviorIrLoadMember loadMember => $"{loadMember.Target} = LoadMember {loadMember.Member}",
            BehaviorIrBinaryOp binaryOp => $"{binaryOp.Target} = BinaryOp {binaryOp.Operator} {binaryOp.Left}, {binaryOp.Right}",
            BehaviorIrMakeStruct makeStruct => $"{makeStruct.Target} = MakeStruct {makeStruct.Type}({string.Join(", ", makeStruct.Arguments)})",
            BehaviorIrCallFunction { Target: not null } call => $"{call.Target} = Call {call.FunctionId}({string.Join(", ", call.Arguments)})",
            BehaviorIrCallFunction call => $"Call {call.FunctionId}({string.Join(", ", call.Arguments)})",
            BehaviorIrBranch branch => $"Branch {branch.Condition} then {branch.ThenBlock} else {branch.ElseBlock}",
            BehaviorIrJump jump => $"Jump {jump.TargetBlock}",
            BehaviorIrReturn => "Return",
            BehaviorIrDeclareLocal declareLocal => $"DeclareLocal {declareLocal.LocalName}",
            BehaviorIrStoreLocal storeLocal => $"StoreLocal {storeLocal.LocalName}, {storeLocal.Value}",
            BehaviorIrAssign assign => $"Assign {assign.TargetExpression}, {assign.Value}",
            BehaviorIrDebugWatch debugWatch => $"DebugWatch {debugWatch.Name}, {debugWatch.Value}",
            BehaviorIrUnsupportedStatement unsupported => $"UnsupportedStatement {unsupported.Kind}",
            BehaviorIrUnsupportedExpression unsupported => $"{unsupported.Target} = UnsupportedExpression {unsupported.Expression}",
            _ => instruction.ToString() ?? string.Empty
        };
    }

    public static string FormatSource(BehaviorSourceSpan source)
    {
        return source.Line <= 0
            ? source.FileName
            : $"{source.FileName}:{source.Line}:{source.Column}";
    }
}

public static class BehaviorIrReporter
{
    public static void Write(BehaviorIrModule module, TextWriter writer)
    {
        writer.WriteLine($"BehaviorModule {module.BehaviorId}");
        writer.WriteLine("Fields:");

        if (module.Fields.Count == 0)
        {
            writer.WriteLine("  <none>");
        }
        else
        {
            foreach (var field in module.Fields)
            {
                var initializer = field.InitialValue is null ? string.Empty : $" = {field.InitialValue}";
                writer.WriteLine($"  {field.Name} : {field.Type}{initializer}");
            }
        }

        foreach (var function in module.Functions)
        {
            var parameters = string.Join(
                ", ",
                function.Parameters.Select(parameter => $"{parameter.Name}: {parameter.Type}"));

            writer.WriteLine($"Function {function.Name}({parameters})");

            foreach (var block in function.Blocks)
            {
                writer.WriteLine($"Block {block.Name}:");

                foreach (var instruction in block.Instructions)
                {
                    writer.WriteLine($"  {BehaviorIrText.FormatWithSource(instruction)}");
                }
            }
        }
    }
}

public sealed record BehaviorIrVerificationVec3(float X, float Y, float Z)
{
    public override string ToString()
    {
        return $"Vec3({X.ToString(CultureInfo.InvariantCulture)}, {Y.ToString(CultureInfo.InvariantCulture)}, {Z.ToString(CultureInfo.InvariantCulture)})";
    }
}

public sealed record BehaviorIrObservedCall(string FunctionId, IReadOnlyList<object?> Arguments);

public sealed record BehaviorIrVerificationResult(IReadOnlyList<BehaviorIrObservedCall> Calls);

public sealed class BehaviorIrVerifier
{
    private readonly IReadOnlySet<string> downKeys;

    public BehaviorIrVerifier(IReadOnlySet<string> downKeys)
    {
        this.downKeys = downKeys;
    }

    public BehaviorIrVerificationResult Execute(
        BehaviorIrModule module,
        string functionName,
        IReadOnlyDictionary<string, object?> parameters)
    {
        var function = module.Functions.Single(candidate => candidate.Name == functionName);
        var blockMap = function.Blocks.ToDictionary(block => block.Name, StringComparer.Ordinal);
        var fields = module.Fields.ToDictionary(
            field => field.Name,
            field => ParseFieldInitialValue(field),
            StringComparer.Ordinal);
        var locals = new Dictionary<string, object?>(parameters, StringComparer.Ordinal);
        var temps = new Dictionary<string, object?>(StringComparer.Ordinal);
        var calls = new List<BehaviorIrObservedCall>();
        var currentBlock = blockMap["entry"];
        var instructionIndex = 0;
        var steps = 0;

        while (steps++ < 1024)
        {
            if (instructionIndex >= currentBlock.Instructions.Count)
            {
                return new BehaviorIrVerificationResult(calls);
            }

            var instruction = currentBlock.Instructions[instructionIndex++];

            switch (instruction)
            {
                case BehaviorIrLoadConst loadConst:
                    temps[loadConst.Target] = ParseLiteral(loadConst.Value);
                    break;

                case BehaviorIrLoadEnum loadEnum:
                    temps[loadEnum.Target] = loadEnum.Value;
                    break;

                case BehaviorIrLoadField loadField:
                    temps[loadField.Target] = fields[loadField.FieldName];
                    break;

                case BehaviorIrLoadLocal loadLocal:
                    temps[loadLocal.Target] = locals[loadLocal.LocalName];
                    break;

                case BehaviorIrLoadSelf loadSelf:
                    temps[loadSelf.Target] = "Self";
                    break;

                case BehaviorIrBinaryOp binaryOp:
                    temps[binaryOp.Target] = ExecuteBinaryOp(binaryOp, temps);
                    break;

                case BehaviorIrMakeStruct makeStruct:
                    temps[makeStruct.Target] = ExecuteMakeStruct(makeStruct, temps);
                    break;

                case BehaviorIrCallFunction call:
                    var result = ExecuteCall(call, temps, calls);
                    if (call.Target is not null)
                    {
                        temps[call.Target] = result;
                    }

                    break;

                case BehaviorIrBranch branch:
                    currentBlock = blockMap[Convert.ToBoolean(temps[branch.Condition], CultureInfo.InvariantCulture)
                        ? branch.ThenBlock
                        : branch.ElseBlock];
                    instructionIndex = 0;
                    break;

                case BehaviorIrJump jump:
                    currentBlock = blockMap[jump.TargetBlock];
                    instructionIndex = 0;
                    break;

                case BehaviorIrReturn:
                    return new BehaviorIrVerificationResult(calls);

                case BehaviorIrStoreLocal storeLocal:
                    locals[storeLocal.LocalName] = temps[storeLocal.Value];
                    break;

                case BehaviorIrDeclareLocal declareLocal:
                    locals[declareLocal.LocalName] = null;
                    break;

                case BehaviorIrDebugWatch:
                    break;
            }
        }

        throw new InvalidOperationException("IR verification step limit exceeded.");
    }

    public static BehaviorIrVerificationResult ExecuteUpdate(
        BehaviorIrModule module,
        float delta,
        IReadOnlySet<string> downKeys)
    {
        var verifier = new BehaviorIrVerifier(downKeys);
        return verifier.Execute(
            module,
            "Update",
            new Dictionary<string, object?> { ["delta"] = delta });
    }

    private object? ExecuteCall(
        BehaviorIrCallFunction call,
        IReadOnlyDictionary<string, object?> temps,
        List<BehaviorIrObservedCall> calls)
    {
        var arguments = call.Arguments.Select(argument => temps[argument]).ToArray();

        return call.FunctionId switch
        {
            "asharia.input.keyDown" => downKeys.Contains((string)arguments[0]!),
            "asharia.transform.translate" => RecordCall(call.FunctionId, arguments, calls),
            _ => throw new InvalidOperationException($"Unsupported verification call '{call.FunctionId}'.")
        };
    }

    private static object? RecordCall(
        string functionId,
        IReadOnlyList<object?> arguments,
        List<BehaviorIrObservedCall> calls)
    {
        calls.Add(new BehaviorIrObservedCall(functionId, arguments.ToArray()));
        return null;
    }

    private static object? ExecuteMakeStruct(
        BehaviorIrMakeStruct makeStruct,
        IReadOnlyDictionary<string, object?> temps)
    {
        if (makeStruct.Type == "Vec3")
        {
            return new BehaviorIrVerificationVec3(
                Convert.ToSingle(temps[makeStruct.Arguments[0]], CultureInfo.InvariantCulture),
                Convert.ToSingle(temps[makeStruct.Arguments[1]], CultureInfo.InvariantCulture),
                Convert.ToSingle(temps[makeStruct.Arguments[2]], CultureInfo.InvariantCulture));
        }

        throw new InvalidOperationException($"Unsupported struct '{makeStruct.Type}'.");
    }

    private static object ExecuteBinaryOp(
        BehaviorIrBinaryOp binaryOp,
        IReadOnlyDictionary<string, object?> temps)
    {
        return binaryOp.Operator switch
        {
            "Multiply" => Convert.ToSingle(temps[binaryOp.Left], CultureInfo.InvariantCulture) *
                Convert.ToSingle(temps[binaryOp.Right], CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unsupported binary operator '{binaryOp.Operator}'.")
        };
    }

    private static object? ParseFieldInitialValue(BehaviorIrField field)
    {
        return field.InitialValue is null ? GetDefaultValue(field.Type) : ParseLiteral(field.InitialValue);
    }

    private static object? GetDefaultValue(string type)
    {
        return type switch
        {
            "float" => 0f,
            "int" => 0,
            "bool" => false,
            "string" => string.Empty,
            _ => null
        };
    }

    private static object ParseLiteral(string text)
    {
        if (text.EndsWith("f", StringComparison.OrdinalIgnoreCase))
        {
            return float.Parse(text[..^1], CultureInfo.InvariantCulture);
        }

        if (text.StartsWith("\"", StringComparison.Ordinal) && text.EndsWith("\"", StringComparison.Ordinal))
        {
            return text[1..^1];
        }

        if (bool.TryParse(text, out var boolValue))
        {
            return boolValue;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        return text;
    }
}

public static class BehaviorIrVerificationReporter
{
    public static void Write(BehaviorIrVerificationResult result, TextWriter writer)
    {
        writer.WriteLine("ObservedCalls:");

        if (result.Calls.Count == 0)
        {
            writer.WriteLine("  <none>");
            return;
        }

        foreach (var call in result.Calls)
        {
            writer.WriteLine($"  {call.FunctionId}({string.Join(", ", call.Arguments)})");
        }
    }
}
