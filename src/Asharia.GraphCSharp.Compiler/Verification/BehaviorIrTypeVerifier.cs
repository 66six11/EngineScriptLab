using ScriptLab.GraphCSharp;

namespace ScriptLab;

public static class BehaviorIrTypeVerifier
{
    public static void Verify(BehaviorIrModule module)
    {
        var errors = new List<string>();
        var fieldsById = module.Fields.ToDictionary(
            field => field.FieldId,
            EqualityComparer<FieldId>.Default);

        foreach (var field in module.Fields)
        {
            RequireKnownType(field.Type, $"Field '{field.Name}'", errors);
        }

        foreach (var function in module.Functions)
        {
            VerifyFunction(function, fieldsById, errors);
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Behavior IR type verification failed: {string.Join("; ", errors)}");
        }
    }

    private static void VerifyFunction(
        BehaviorIrFunction function,
        IReadOnlyDictionary<FieldId, BehaviorIrField> fieldsById,
        ICollection<string> errors)
    {
        var blockNames = function.Blocks
            .Select(block => block.Name)
            .ToHashSet(StringComparer.Ordinal);
        var locals = function.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameter.Type,
            StringComparer.Ordinal);
        var temps = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var parameter in function.Parameters)
        {
            RequireKnownType(parameter.Type, $"Parameter '{function.Name}.{parameter.Name}'", errors);
        }

        foreach (var block in function.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                VerifyInstruction(instruction, blockNames, fieldsById, locals, temps, errors);
            }
        }
    }

    private static void VerifyInstruction(
        BehaviorIrInstruction instruction,
        IReadOnlySet<string> blockNames,
        IReadOnlyDictionary<FieldId, BehaviorIrField> fieldsById,
        Dictionary<string, string> locals,
        Dictionary<string, string> temps,
        ICollection<string> errors)
    {
        switch (instruction)
        {
            case BehaviorIrValueInstruction valueInstruction:
                VerifyValueInstruction(valueInstruction, fieldsById, locals, temps, errors);
                break;

            case BehaviorIrCallFunction call:
                VerifyCall(call, temps, errors);
                break;

            case BehaviorIrBranch branch:
                RequireTempType(branch.Condition, temps, "bool", "Branch condition", errors);
                RequireBlock(branch.ThenBlock, blockNames, instruction, errors);
                RequireBlock(branch.ElseBlock, blockNames, instruction, errors);
                break;

            case BehaviorIrJump jump:
                RequireBlock(jump.TargetBlock, blockNames, instruction, errors);
                break;

            case BehaviorIrDeclareLocal declareLocal:
                RequireKnownType(declareLocal.Type, $"Local '{declareLocal.LocalName}'", errors);
                locals[declareLocal.LocalName] = declareLocal.Type;
                break;

            case BehaviorIrStoreLocal storeLocal:
                RequireLocalType(storeLocal.LocalName, locals, out var localType, errors);
                RequireTempType(storeLocal.Value, temps, localType, $"StoreLocal '{storeLocal.LocalName}'", errors);
                break;

            case BehaviorIrAssign assign:
                RequireTemp(assign.Value, temps, $"Assign '{assign.TargetExpression}'", errors);
                break;

            case BehaviorIrDebugWatch debugWatch:
                RequireTemp(debugWatch.Value, temps, $"DebugWatch '{debugWatch.Name}'", errors);
                break;
        }
    }

    private static void VerifyValueInstruction(
        BehaviorIrValueInstruction instruction,
        IReadOnlyDictionary<FieldId, BehaviorIrField> fieldsById,
        IReadOnlyDictionary<string, string> locals,
        Dictionary<string, string> temps,
        ICollection<string> errors)
    {
        RequireKnownType(instruction.Type, $"Temp '{instruction.Target}'", errors);

        switch (instruction)
        {
            case BehaviorIrLoadField loadField:
                if (!fieldsById.TryGetValue(loadField.FieldId, out var field))
                {
                    errors.Add($"LoadField '{loadField.FieldName}' references unknown field id '{loadField.FieldId}'.");
                }
                else if (!TypeMatches(field.Type, loadField.Type))
                {
                    errors.Add(
                        $"LoadField '{loadField.FieldName}' has type '{loadField.Type}', but field '#{field.FieldId}' is '{field.Type}'.");
                }

                break;

            case BehaviorIrLoadLocal loadLocal:
                RequireLocalType(loadLocal.LocalName, locals, out var localType, errors);
                if (localType.Length > 0 && !TypeMatches(localType, loadLocal.Type))
                {
                    errors.Add(
                        $"LoadLocal '{loadLocal.LocalName}' has type '{loadLocal.Type}', but local is '{localType}'.");
                }

                break;

            case BehaviorIrLoadSelf loadSelf when !TypeMatches("EntityRef", loadSelf.Type):
                errors.Add($"LoadSelf has type '{loadSelf.Type}', but Self is 'EntityRef'.");
                break;

            case BehaviorIrBinaryOp binaryOp:
                VerifyBinaryOp(binaryOp, temps, errors);
                break;

            case BehaviorIrMakeStruct makeStruct:
                VerifyMakeStruct(makeStruct, temps, errors);
                break;
        }

        DefineTemp(instruction.Target, instruction.Type, temps, errors);
    }

    private static void VerifyCall(
        BehaviorIrCallFunction call,
        Dictionary<string, string> temps,
        ICollection<string> errors)
    {
        if (call.Target is not null && string.IsNullOrWhiteSpace(call.ReturnType))
        {
            errors.Add($"Call '{call.FunctionId}' writes temp '{call.Target}' without a return type.");
        }

        if (call.Target is null && !string.IsNullOrWhiteSpace(call.ReturnType))
        {
            errors.Add($"Call '{call.FunctionId}' has return type '{call.ReturnType}' without a target temp.");
        }

        var binding = GraphCSharpBindingRegistry.Functions
            .FirstOrDefault(candidate => candidate.FunctionId == call.FunctionId);
        if (binding is null)
        {
            errors.Add($"Call '{call.FunctionId}' is not registered.");
        }
        else
        {
            VerifyCallBinding(call, binding, temps, errors);
        }

        if (call.Target is not null && call.ReturnType is not null)
        {
            DefineTemp(call.Target, call.ReturnType, temps, errors);
        }
    }

    private static void VerifyCallBinding(
        BehaviorIrCallFunction call,
        GraphCSharpFunctionBinding binding,
        IReadOnlyDictionary<string, string> temps,
        ICollection<string> errors)
    {
        if (call.Arguments.Count != binding.Parameters.Count)
        {
            errors.Add(
                $"Call '{call.FunctionId}' expects {binding.Parameters.Count} argument(s), but received {call.Arguments.Count}.");
            return;
        }

        if (call.Target is not null &&
            binding.ReturnType is not null &&
            binding.ReturnType != "*" &&
            !TypeMatches(binding.ReturnType, call.ReturnType ?? string.Empty))
        {
            errors.Add(
                $"Call '{call.FunctionId}' returns '{call.ReturnType}', but binding returns '{binding.ReturnType}'.");
        }

        for (var index = 0; index < call.Arguments.Count; index++)
        {
            var parameter = binding.Parameters[index];
            if (!temps.TryGetValue(call.Arguments[index], out var argumentType))
            {
                errors.Add($"Call '{call.FunctionId}' argument {index + 1} references unknown temp '{call.Arguments[index]}'.");
                continue;
            }

            if (parameter.TypeName == "*" || TypeMatches(parameter.TypeName, argumentType))
            {
                continue;
            }

            errors.Add(
                $"Call '{call.FunctionId}' argument {index + 1} expects '{parameter.TypeName}', but received '{argumentType}'.");
        }
    }

    private static void VerifyBinaryOp(
        BehaviorIrBinaryOp binaryOp,
        IReadOnlyDictionary<string, string> temps,
        ICollection<string> errors)
    {
        var leftType = RequireTemp(binaryOp.Left, temps, $"BinaryOp '{binaryOp.Operator}'", errors);
        var rightType = RequireTemp(binaryOp.Right, temps, $"BinaryOp '{binaryOp.Operator}'", errors);

        switch (binaryOp.Operator)
        {
            case "Multiply":
            case "Add":
            case "Subtract":
            case "Divide":
            case "Modulo":
                RequireNumericTemp(binaryOp.Left, leftType, binaryOp.Operator, errors);
                RequireNumericTemp(binaryOp.Right, rightType, binaryOp.Operator, errors);
                if (!IsNumericType(binaryOp.Type))
                {
                    errors.Add($"BinaryOp '{binaryOp.Operator}' has non-numeric result type '{binaryOp.Type}'.");
                }

                break;

            case "Equals":
            case "NotEquals":
                if (!TypeMatches("bool", binaryOp.Type))
                {
                    errors.Add($"BinaryOp '{binaryOp.Operator}' must produce 'bool', but produced '{binaryOp.Type}'.");
                }

                break;

            case "LessThan":
            case "LessThanOrEqual":
            case "GreaterThan":
            case "GreaterThanOrEqual":
                RequireNumericTemp(binaryOp.Left, leftType, binaryOp.Operator, errors);
                RequireNumericTemp(binaryOp.Right, rightType, binaryOp.Operator, errors);
                if (!TypeMatches("bool", binaryOp.Type))
                {
                    errors.Add($"BinaryOp '{binaryOp.Operator}' must produce 'bool', but produced '{binaryOp.Type}'.");
                }

                break;
        }
    }

    private static void VerifyMakeStruct(
        BehaviorIrMakeStruct makeStruct,
        IReadOnlyDictionary<string, string> temps,
        ICollection<string> errors)
    {
        if (!GraphCSharpRuleSet.IsConstructibleValueType(makeStruct.Type))
        {
            errors.Add($"MakeStruct '{makeStruct.Type}' is not a registered constructible value type.");
        }

        if (makeStruct.Type == "Vec3")
        {
            if (makeStruct.Arguments.Count != 3)
            {
                errors.Add($"MakeStruct 'Vec3' expects 3 argument(s), but received {makeStruct.Arguments.Count}.");
                return;
            }

            foreach (var argument in makeStruct.Arguments)
            {
                RequireTempType(argument, temps, "float", "MakeStruct 'Vec3'", errors);
            }

            return;
        }

        foreach (var argument in makeStruct.Arguments)
        {
            RequireTemp(argument, temps, $"MakeStruct '{makeStruct.Type}'", errors);
        }
    }

    private static string RequireTemp(
        string temp,
        IReadOnlyDictionary<string, string> temps,
        string owner,
        ICollection<string> errors)
    {
        if (temps.TryGetValue(temp, out var type))
        {
            return type;
        }

        errors.Add($"{owner} references unknown temp '{temp}'.");
        return string.Empty;
    }

    private static void RequireTempType(
        string temp,
        IReadOnlyDictionary<string, string> temps,
        string expectedType,
        string owner,
        ICollection<string> errors)
    {
        var actualType = RequireTemp(temp, temps, owner, errors);
        if (actualType.Length > 0 && !TypeMatches(expectedType, actualType))
        {
            errors.Add($"{owner} expects '{expectedType}', but temp '{temp}' is '{actualType}'.");
        }
    }

    private static void RequireNumericTemp(
        string temp,
        string type,
        string operation,
        ICollection<string> errors)
    {
        if (type.Length > 0 && !IsNumericType(type))
        {
            errors.Add($"BinaryOp '{operation}' expects numeric temp '{temp}', but received '{type}'.");
        }
    }

    private static void RequireLocalType(
        string localName,
        IReadOnlyDictionary<string, string> locals,
        out string localType,
        ICollection<string> errors)
    {
        if (locals.TryGetValue(localName, out localType!))
        {
            return;
        }

        localType = string.Empty;
        errors.Add($"Local '{localName}' is not declared.");
    }

    private static void RequireBlock(
        string blockName,
        IReadOnlySet<string> blockNames,
        BehaviorIrInstruction instruction,
        ICollection<string> errors)
    {
        if (!blockNames.Contains(blockName))
        {
            errors.Add($"{instruction.GetType().Name} references unknown block '{blockName}'.");
        }
    }

    private static void RequireKnownType(string type, string owner, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(type) || type == "var")
        {
            errors.Add($"{owner} has unresolved type '{type}'.");
        }
    }

    private static void DefineTemp(
        string temp,
        string type,
        IDictionary<string, string> temps,
        ICollection<string> errors)
    {
        if (!temps.TryAdd(temp, type))
        {
            errors.Add($"Temp '{temp}' is defined more than once.");
        }
    }

    private static bool TypeMatches(string expectedType, string actualType)
    {
        return string.Equals(expectedType, actualType, StringComparison.Ordinal);
    }

    private static bool IsNumericType(string type)
    {
        return type is "int" or "float";
    }
}
