namespace ScriptLab;

public static class ConsoleScriptReporter
{
    public static void Write(ScriptParseResult result, TextWriter writer)
    {
        writer.WriteLine($"Script: {Path.GetFileName(result.Path)}");

        WriteDiagnostics(
            "Syntax",
            result.Diagnostics.Where(diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal)),
            writer);

        WriteDiagnostics(
            "Graph C#",
            result.Diagnostics.Where(diagnostic => diagnostic.Id.StartsWith("AGC", StringComparison.Ordinal)),
            writer);

        if (result.Behavior is null)
        {
            writer.WriteLine("Behavior: <none>");
            return;
        }

        writer.WriteLine($"Behavior: {result.Behavior.Name}");
        writer.WriteLine($"BehaviorId: {result.Behavior.Id} [{result.Behavior.IdSource}]");

        if (result.Behavior.FormerlyBehaviorIds.Count > 0)
        {
            writer.WriteLine($"FormerlyBehavior: {string.Join(", ", result.Behavior.FormerlyBehaviorIds)}");
        }

        WriteFields(result.Behavior, writer);
        WriteMethods(result.Behavior, writer);
    }

    private static void WriteFields(ScriptBehaviorSummary behavior, TextWriter writer)
    {
        writer.WriteLine("Fields:");

        if (behavior.Fields.Count == 0)
        {
            writer.WriteLine("  <none>");
            return;
        }

        foreach (var field in behavior.Fields)
        {
            writer.WriteLine($"  {field.Accessibility} {field.Name} : {field.Type} [{field.Serialization}]");
        }
    }

    private static void WriteDiagnostics(string label, IEnumerable<ScriptDiagnostic> diagnostics, TextWriter writer)
    {
        var diagnosticList = diagnostics.ToArray();

        if (diagnosticList.Length == 0)
        {
            writer.WriteLine($"{label}: OK");
            return;
        }

        writer.WriteLine($"{label}: {diagnosticList.Length} diagnostic(s)");

        foreach (var diagnostic in diagnosticList)
        {
            writer.WriteLine(
                $"  {diagnostic.Id} {diagnostic.Stage} {diagnostic.Severity}: {diagnostic.Message} ({diagnostic.FileName}:{diagnostic.Line}:{diagnostic.Column})");
        }
    }

    private static void WriteMethods(ScriptBehaviorSummary behavior, TextWriter writer)
    {
        writer.WriteLine("Methods:");

        if (behavior.Methods.Count == 0)
        {
            writer.WriteLine("  <none>");
            return;
        }

        foreach (var method in behavior.Methods)
        {
            writer.WriteLine($"  {method.Signature}");
            WriteBody(method.Body, writer, 4);
        }
    }

    private static void WriteBody(IReadOnlyList<ScriptBodyNodeSummary> nodes, TextWriter writer, int indent)
    {
        foreach (var node in nodes)
        {
            var text = string.IsNullOrWhiteSpace(node.Text) ? node.Kind : $"{node.Kind} {node.Text}";
            writer.WriteLine($"{new string(' ', indent)}{text}");
            WriteBody(node.Children, writer, indent + 2);
        }
    }
}
