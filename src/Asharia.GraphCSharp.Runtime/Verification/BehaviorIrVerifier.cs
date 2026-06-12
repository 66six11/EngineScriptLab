using System.Globalization;

namespace ScriptLab;

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