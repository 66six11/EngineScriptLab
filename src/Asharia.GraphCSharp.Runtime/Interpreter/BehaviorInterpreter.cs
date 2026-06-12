namespace ScriptLab;

public sealed class BehaviorInterpreter
{
    public ScriptExecutionResult ExecuteEvent(
        BehaviorProgram program,
        ScriptInstance instance,
        string eventName,
        IReadOnlyDictionary<string, object?> parameters,
        ScriptExecutionContext context)
    {
        if (!program.TryGetFunction(eventName, out var function))
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.MissingFunction,
                $"Behavior '{program.BehaviorId}' does not contain event '{eventName}'.",
                null,
                BehaviorSourceSpan.Generated));
            return ScriptExecutionResult.FromContext(context);
        }

        var frame = new InterpreterFrame(function, parameters);
        var blockMap = function.Blocks.ToDictionary(block => block.Name, StringComparer.Ordinal);

        if (!blockMap.TryGetValue("entry", out var currentBlock))
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.MissingBlock,
                $"Event '{eventName}' does not contain an entry block.",
                null,
                BehaviorSourceSpan.Generated));
            return ScriptExecutionResult.FromContext(context);
        }

        var instructionIndex = 0;
        var steps = 0;

        while (steps++ < context.StepLimit)
        {
            if (instructionIndex >= currentBlock.Instructions.Count)
            {
                return ScriptExecutionResult.FromContext(context);
            }

            var instruction = currentBlock.Instructions[instructionIndex++];

            switch (instruction)
            {
                case BehaviorIrLoadConst loadConst:
                    frame.SetTemp(loadConst.Target, ScriptRuntimeValueParser.ParseLiteral(loadConst.Value));
                    break;

                case BehaviorIrLoadEnum loadEnum:
                    frame.SetTemp(loadEnum.Target, loadEnum.Value);
                    break;

                case BehaviorIrLoadField loadField:
                    if (!TryLoadField(loadField, instance, frame, context))
                    {
                        return ScriptExecutionResult.FromContext(context);
                    }

                    break;

                case BehaviorIrLoadLocal loadLocal:
                    if (!TryLoadLocal(loadLocal, frame, context))
                    {
                        return ScriptExecutionResult.FromContext(context);
                    }

                    break;

                case BehaviorIrLoadSelf loadSelf:
                    frame.SetTemp(loadSelf.Target, new ScriptRuntimeEntityRef(instance.EntityId));
                    break;

                case BehaviorIrBinaryOp binaryOp:
                    if (!TryExecuteBinaryOp(binaryOp, frame, context))
                    {
                        return ScriptExecutionResult.FromContext(context);
                    }

                    break;

                case BehaviorIrMakeStruct makeStruct:
                    if (!TryExecuteMakeStruct(makeStruct, frame, context))
                    {
                        return ScriptExecutionResult.FromContext(context);
                    }

                    break;

                case BehaviorIrCallFunction call:
                    if (!TryExecuteCall(call, frame, context))
                    {
                        return ScriptExecutionResult.FromContext(context);
                    }

                    break;

                case BehaviorIrBranch branch:
                    if (!TryMoveToBranch(branch, frame, blockMap, context, out currentBlock))
                    {
                        return ScriptExecutionResult.FromContext(context);
                    }

                    instructionIndex = 0;
                    break;

                case BehaviorIrJump jump:
                    if (!TryMoveToBlock(jump.TargetBlock, jump, blockMap, context, out currentBlock))
                    {
                        return ScriptExecutionResult.FromContext(context);
                    }

                    instructionIndex = 0;
                    break;

                case BehaviorIrReturn:
                    return ScriptExecutionResult.FromContext(context);

                case BehaviorIrDeclareLocal declareLocal:
                    frame.DeclareLocal(
                        declareLocal.LocalName,
                        ScriptRuntimeValueParser.GetDefaultValue(declareLocal.Type));
                    break;

                case BehaviorIrStoreLocal storeLocal:
                    if (!TryStoreLocal(storeLocal, frame, context))
                    {
                        return ScriptExecutionResult.FromContext(context);
                    }

                    break;

                case BehaviorIrDebugWatch:
                    break;

                default:
                    ReportUnsupportedInstruction(instruction, context);
                    return ScriptExecutionResult.FromContext(context);
            }
        }

        context.Report(new RuntimeDiagnostic(
            RuntimeDiagnostic.StepLimitExceeded,
            $"Event '{eventName}' exceeded the interpreter step limit of {context.StepLimit}.",
            null,
            BehaviorSourceSpan.Generated));
        return ScriptExecutionResult.FromContext(context);
    }

    public ScriptExecutionResult ExecuteEvent(
        BehaviorProgram program,
        ScriptInstance instance,
        string eventName,
        IReadOnlyDictionary<string, object?> parameters)
    {
        return ExecuteEvent(program, instance, eventName, parameters, new ScriptExecutionContext());
    }

    private static bool TryLoadField(
        BehaviorIrLoadField loadField,
        ScriptInstance instance,
        InterpreterFrame frame,
        ScriptExecutionContext context)
    {
        if (!instance.TryGetField(loadField.FieldId, out var value))
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.MissingValue,
                $"Field '{loadField.FieldName}' with id '{loadField.FieldId}' does not exist on the script instance.",
                loadField.DebugSiteId,
                loadField.Source));
            return false;
        }

        frame.SetTemp(loadField.Target, value);
        return true;
    }

    private static bool TryLoadLocal(
        BehaviorIrLoadLocal loadLocal,
        InterpreterFrame frame,
        ScriptExecutionContext context)
    {
        if (!frame.TryGetLocal(loadLocal.LocalName, out var value))
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.MissingValue,
                $"Local '{loadLocal.LocalName}' is not available.",
                loadLocal.DebugSiteId,
                loadLocal.Source));
            return false;
        }

        frame.SetTemp(loadLocal.Target, value);
        return true;
    }

    private static bool TryStoreLocal(
        BehaviorIrStoreLocal storeLocal,
        InterpreterFrame frame,
        ScriptExecutionContext context)
    {
        if (!TryGetTemp(storeLocal.Value, storeLocal, frame, context, out var value))
        {
            return false;
        }

        frame.SetLocal(storeLocal.LocalName, value);
        return true;
    }

    private static bool TryExecuteBinaryOp(
        BehaviorIrBinaryOp binaryOp,
        InterpreterFrame frame,
        ScriptExecutionContext context)
    {
        if (!TryGetTemp(binaryOp.Left, binaryOp, frame, context, out var left) ||
            !TryGetTemp(binaryOp.Right, binaryOp, frame, context, out var right))
        {
            return false;
        }

        try
        {
            object? result = binaryOp.Operator switch
            {
                "Multiply" => ScriptRuntimeValueParser.ToSingle(left) * ScriptRuntimeValueParser.ToSingle(right),
                "Add" => ScriptRuntimeValueParser.ToSingle(left) + ScriptRuntimeValueParser.ToSingle(right),
                "Subtract" => ScriptRuntimeValueParser.ToSingle(left) - ScriptRuntimeValueParser.ToSingle(right),
                "Divide" => ScriptRuntimeValueParser.ToSingle(left) / ScriptRuntimeValueParser.ToSingle(right),
                "Modulo" => ScriptRuntimeValueParser.ToSingle(left) % ScriptRuntimeValueParser.ToSingle(right),
                "Equals" => Equals(left, right),
                "NotEquals" => !Equals(left, right),
                "LessThan" => ScriptRuntimeValueParser.ToSingle(left) < ScriptRuntimeValueParser.ToSingle(right),
                "LessThanOrEqual" => ScriptRuntimeValueParser.ToSingle(left) <= ScriptRuntimeValueParser.ToSingle(right),
                "GreaterThan" => ScriptRuntimeValueParser.ToSingle(left) > ScriptRuntimeValueParser.ToSingle(right),
                "GreaterThanOrEqual" => ScriptRuntimeValueParser.ToSingle(left) >= ScriptRuntimeValueParser.ToSingle(right),
                _ => null
            };

            if (result is null && binaryOp.Operator is not "Equals" and not "NotEquals")
            {
                ReportUnsupportedInstruction(binaryOp, context);
                return false;
            }

            frame.SetTemp(binaryOp.Target, result);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.InvalidRuntimeValue,
                $"Binary operator '{binaryOp.Operator}' received incompatible values.",
                binaryOp.DebugSiteId,
                binaryOp.Source));
            return false;
        }
    }

    private static bool TryExecuteMakeStruct(
        BehaviorIrMakeStruct makeStruct,
        InterpreterFrame frame,
        ScriptExecutionContext context)
    {
        var arguments = new object?[makeStruct.Arguments.Count];

        for (var i = 0; i < makeStruct.Arguments.Count; i++)
        {
            if (!TryGetTemp(makeStruct.Arguments[i], makeStruct, frame, context, out arguments[i]))
            {
                return false;
            }
        }

        if (makeStruct.Type != "Vec3" || arguments.Length != 3)
        {
            ReportUnsupportedInstruction(makeStruct, context);
            return false;
        }

        try
        {
            frame.SetTemp(makeStruct.Target, new ScriptRuntimeVec3(
                ScriptRuntimeValueParser.ToSingle(arguments[0]),
                ScriptRuntimeValueParser.ToSingle(arguments[1]),
                ScriptRuntimeValueParser.ToSingle(arguments[2])));
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.InvalidRuntimeValue,
                "Vec3 construction received incompatible values.",
                makeStruct.DebugSiteId,
                makeStruct.Source));
            return false;
        }
    }

    private static bool TryExecuteCall(
        BehaviorIrCallFunction call,
        InterpreterFrame frame,
        ScriptExecutionContext context)
    {
        var arguments = new object?[call.Arguments.Count];

        for (var i = 0; i < call.Arguments.Count; i++)
        {
            if (!TryGetTemp(call.Arguments[i], call, frame, context, out arguments[i]))
            {
                return false;
            }
        }

        if (!context.FunctionRegistry.TryInvoke(call, arguments, context, out var result))
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.UnsupportedFunction,
                $"Runtime function '{call.FunctionId}' is not registered.",
                call.DebugSiteId,
                call.Source));
            return false;
        }

        if (call.Target is not null)
        {
            frame.SetTemp(call.Target, result);
        }

        return true;
    }

    private static bool TryMoveToBranch(
        BehaviorIrBranch branch,
        InterpreterFrame frame,
        IReadOnlyDictionary<string, BehaviorIrBlock> blockMap,
        ScriptExecutionContext context,
        out BehaviorIrBlock block)
    {
        block = null!;

        if (!TryGetTemp(branch.Condition, branch, frame, context, out var condition))
        {
            return false;
        }

        if (condition is not bool boolCondition)
        {
            context.Report(new RuntimeDiagnostic(
                RuntimeDiagnostic.InvalidRuntimeValue,
                "Branch condition must be a bool value.",
                branch.DebugSiteId,
                branch.Source));
            return false;
        }

        var targetBlock = boolCondition ? branch.ThenBlock : branch.ElseBlock;
        return TryMoveToBlock(targetBlock, branch, blockMap, context, out block);
    }

    private static bool TryMoveToBlock(
        string targetBlock,
        BehaviorIrInstruction instruction,
        IReadOnlyDictionary<string, BehaviorIrBlock> blockMap,
        ScriptExecutionContext context,
        out BehaviorIrBlock block)
    {
        var found = blockMap.TryGetValue(targetBlock, out var candidate);
        block = candidate!;

        if (found)
        {
            return true;
        }

        context.Report(new RuntimeDiagnostic(
            RuntimeDiagnostic.MissingBlock,
            $"Block '{targetBlock}' does not exist.",
            instruction.DebugSiteId,
            instruction.Source));
        return false;
    }

    private static bool TryGetTemp(
        string tempName,
        BehaviorIrInstruction instruction,
        InterpreterFrame frame,
        ScriptExecutionContext context,
        out object? value)
    {
        if (frame.TryGetTemp(tempName, out value))
        {
            return true;
        }

        context.Report(new RuntimeDiagnostic(
            RuntimeDiagnostic.MissingValue,
            $"Temp '{tempName}' is not available.",
            instruction.DebugSiteId,
            instruction.Source));
        return false;
    }

    private static void ReportUnsupportedInstruction(
        BehaviorIrInstruction instruction,
        ScriptExecutionContext context)
    {
        context.Report(new RuntimeDiagnostic(
            RuntimeDiagnostic.UnsupportedInstruction,
            $"Instruction '{instruction.GetType().Name}' is not supported by the interpreter runtime.",
            instruction.DebugSiteId,
            instruction.Source));
    }
}
