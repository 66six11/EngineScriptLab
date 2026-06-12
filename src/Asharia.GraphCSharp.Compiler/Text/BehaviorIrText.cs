namespace ScriptLab;

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
            BehaviorIrLoadField loadField => $"{loadField.Target} = LoadField #{loadField.FieldId} {loadField.FieldName}",
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
                writer.WriteLine($"  #{field.FieldId} {field.Name} : {field.Type}{initializer}");
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
