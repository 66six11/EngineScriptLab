using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab.GraphCSharp;

namespace ScriptLab;

public sealed record BehaviorIrModule(
    string BehaviorId,
    IReadOnlyList<BehaviorIrField> Fields,
    IReadOnlyList<BehaviorIrFunction> Functions);

public sealed record BehaviorIrField(string Name, string Type);

public sealed record BehaviorIrFunction(
    string Name,
    IReadOnlyList<BehaviorIrParameter> Parameters,
    IReadOnlyList<BehaviorIrBlock> Blocks);

public sealed record BehaviorIrParameter(string Name, string Type);

public sealed record BehaviorIrBlock(string Name, IReadOnlyList<string> Instructions);

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

        return new BehaviorIrModule(
            parseResult.Behavior.Id,
            parseResult.Behavior.Fields.Select(field => new BehaviorIrField(field.Name, field.Type)).ToArray(),
            LowerFunctions(behaviorClass, parseResult.Behavior));
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
        var builder = new FunctionBuilder(fieldNames);

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
        private readonly List<MutableBlock> blocks = new();
        private int tempIndex;
        private int blockIndex;
        private MutableBlock currentBlock;

        public FunctionBuilder(ISet<string> fieldNames)
        {
            this.fieldNames = fieldNames;
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
                    currentBlock.Instructions.Add("Return");
                    break;

                default:
                    currentBlock.Instructions.Add($"UnsupportedStatement {statement.Kind()}");
                    break;
            }
        }

        public void EnsureCurrentBlockReturns()
        {
            if (currentBlock.Instructions.Count == 0 || currentBlock.Instructions[^1] != "Return")
            {
                currentBlock.Instructions.Add("Return");
            }
        }

        private void LowerIfStatement(IfStatementSyntax ifStatement)
        {
            var condition = LowerExpression(ifStatement.Condition);
            var thenBlock = CreateBlock($"then_{blockIndex++}");
            var exitBlock = CreateBlock($"exit_{blockIndex++}");

            currentBlock.Instructions.Add($"Branch {condition} then {thenBlock.Name} else {exitBlock.Name}");

            currentBlock = thenBlock;
            LowerStatement(ifStatement.Statement);
            currentBlock.Instructions.Add($"Jump {exitBlock.Name}");

            if (ifStatement.Else is not null)
            {
                var elseBlock = CreateBlock($"else_{blockIndex++}");
                currentBlock = elseBlock;
                LowerStatement(ifStatement.Else.Statement);
                currentBlock.Instructions.Add($"Jump {exitBlock.Name}");
            }

            currentBlock = exitBlock;
        }

        private void LowerLocalDeclaration(LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (variable.Initializer is null)
                {
                    currentBlock.Instructions.Add($"DeclareLocal {variable.Identifier.ValueText}");
                    continue;
                }

                var value = LowerExpression(variable.Initializer.Value);
                currentBlock.Instructions.Add($"StoreLocal {variable.Identifier.ValueText}, {value}");
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
                    currentBlock.Instructions.Add($"Assign {assignment.Left}, {value}");
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
                LiteralExpressionSyntax literal => EmitValue($"LoadConst {literal.Token.Text}"),
                IdentifierNameSyntax identifier => LowerIdentifier(identifier),
                MemberAccessExpressionSyntax memberAccess => LowerMemberAccess(memberAccess),
                BinaryExpressionSyntax binary => LowerBinary(binary),
                ObjectCreationExpressionSyntax objectCreation => LowerObjectCreation(objectCreation),
                InvocationExpressionSyntax invocation => LowerInvocation(invocation, emitResult: true),
                ParenthesizedExpressionSyntax parenthesized => LowerExpression(parenthesized.Expression),
                _ => EmitValue($"UnsupportedExpression {expression}")
            };
        }

        private string LowerIdentifier(IdentifierNameSyntax identifier)
        {
            var name = identifier.Identifier.ValueText;

            if (name == "Self")
            {
                return EmitValue("LoadSelf");
            }

            if (fieldNames.Contains(name))
            {
                return EmitValue($"LoadField {name}");
            }

            return EmitValue($"LoadLocal {name}");
        }

        private string LowerMemberAccess(MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Expression is IdentifierNameSyntax typeName && typeName.Identifier.ValueText == "Key")
            {
                return EmitValue($"LoadEnum {memberAccess}");
            }

            return EmitValue($"LoadMember {memberAccess}");
        }

        private string LowerBinary(BinaryExpressionSyntax binary)
        {
            var left = LowerExpression(binary.Left);
            var right = LowerExpression(binary.Right);
            return EmitValue($"BinaryOp {GetBinaryOperator(binary)} {left}, {right}");
        }

        private string LowerObjectCreation(ObjectCreationExpressionSyntax objectCreation)
        {
            var arguments = objectCreation.ArgumentList?.Arguments
                .Select(argument => LowerExpression(argument.Expression))
                .ToArray()
                ?? Array.Empty<string>();

            return EmitValue($"MakeStruct {objectCreation.Type}({string.Join(", ", arguments)})");
        }

        private string LowerInvocation(InvocationExpressionSyntax invocation, bool emitResult)
        {
            var arguments = invocation.ArgumentList.Arguments
                .Select(argument => LowerExpression(argument.Expression))
                .ToArray();
            var functionName = invocation.Expression.ToString();
            if (!GraphCSharpBindingRegistry.TryGetFunctionId(functionName, out var functionId))
            {
                functionId = functionName;
            }

            var call = $"Call {functionId}({string.Join(", ", arguments)})";

            if (emitResult)
            {
                return EmitValue(call);
            }

            currentBlock.Instructions.Add(call);
            return string.Empty;
        }

        private string EmitValue(string instruction)
        {
            var temp = NextTemp();
            currentBlock.Instructions.Add($"{temp} = {instruction}");
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

    private sealed class MutableBlock
    {
        public MutableBlock(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public List<string> Instructions { get; } = new();
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
                writer.WriteLine($"  {field.Name} : {field.Type}");
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
                    writer.WriteLine($"  {instruction}");
                }
            }
        }
    }
}
