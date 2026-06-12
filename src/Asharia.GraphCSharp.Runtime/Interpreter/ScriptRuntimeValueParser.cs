using System.Globalization;

namespace ScriptLab;

internal static class ScriptRuntimeValueParser
{
    public static object? GetDefaultValue(string type)
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

    public static object ParseLiteral(string text)
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

    public static float ToSingle(object? value)
    {
        return Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }
}
