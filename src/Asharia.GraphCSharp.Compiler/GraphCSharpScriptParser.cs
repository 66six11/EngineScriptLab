using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab.GraphCSharp;

namespace ScriptLab;

public static class GraphCSharpScriptParser
{
    public static ScriptParseResult ParseFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var source = File.ReadAllText(fullPath);
        return ParseText(source, fullPath);
    }

    public static ScriptParseResult ParseText(string source, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var tree = CSharpSyntaxTree.ParseText(source, path: fullPath);
        var root = tree.GetCompilationUnitRoot();
        var diagnostics = tree.GetDiagnostics().Select(ToScriptDiagnostic).ToList();
        var semanticModel = diagnostics.Any(diagnostic => diagnostic.Severity == "Error")
            ? null
            : GraphCSharpSemanticModelFactory.CreateSemanticModel(tree);

        if (!diagnostics.Any(diagnostic => diagnostic.Severity == "Error"))
        {
            diagnostics.AddRange(GraphCSharpSubsetAnalyzer.Analyze(tree));
        }

        var behaviorClass = FindBehaviorClass(root);

        return new ScriptParseResult(
            fullPath,
            diagnostics,
            behaviorClass is null ? null : BuildBehaviorSummary(behaviorClass, semanticModel));
    }

    private static ScriptDiagnostic ToScriptDiagnostic(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var start = span.StartLinePosition;

        return new ScriptDiagnostic(
            diagnostic.Id,
            GraphCSharpAnalysisStage.CSharpSyntax,
            diagnostic.Severity.ToString(),
            diagnostic.GetMessage(),
            Path.GetFileName(span.Path),
            start.Line + 1,
            start.Character + 1);
    }

    private static ClassDeclarationSyntax? FindBehaviorClass(CompilationUnitSyntax root)
    {
        return root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(HasBehaviorAttribute)
            ?? root
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(DerivesFromBehaviorComponent);
    }

    private static ScriptBehaviorSummary BuildBehaviorSummary(
        ClassDeclarationSyntax behaviorClass,
        SemanticModel? semanticModel)
    {
        var explicitBehaviorId = GetFirstAttributeArgument(behaviorClass.AttributeLists, "Behavior");
        var behaviorId = explicitBehaviorId ?? GetDefaultBehaviorId(behaviorClass);

        return new ScriptBehaviorSummary(
            behaviorClass.Identifier.ValueText,
            behaviorId,
            explicitBehaviorId is null ? "default" : "explicit",
            GetAttributeArguments(behaviorClass.AttributeLists, "FormerlyBehavior"),
            BuildFields(behaviorClass, semanticModel),
            BuildMethods(behaviorClass, semanticModel));
    }

    private static IReadOnlyList<ScriptFieldSummary> BuildFields(
        ClassDeclarationSyntax behaviorClass,
        SemanticModel? semanticModel)
    {
        return behaviorClass.Members
            .OfType<FieldDeclarationSyntax>()
            .Where(IsBehaviorField)
            .SelectMany(field =>
            {
                var type = GraphCSharpTypeNameResolver.Resolve(
                    field.Declaration.Type,
                    semanticModel,
                    field.Declaration.Type.ToString());
                var accessibility = GetAccessibility(field.Modifiers);
                var serialization = HasExplicitFieldAttribute(field.AttributeLists)
                    ? "explicit"
                    : "public";

                return field.Declaration.Variables.Select(variable =>
                    new ScriptFieldSummary(
                        variable.Identifier.ValueText,
                        TryGetStableFieldId(field.AttributeLists, out var fieldId) ? fieldId : null,
                        type,
                        accessibility,
                        serialization));
            })
            .ToArray();
    }

    private static IReadOnlyList<ScriptMethodSummary> BuildMethods(
        ClassDeclarationSyntax behaviorClass,
        SemanticModel? semanticModel)
    {
        return behaviorClass.Members
            .OfType<MethodDeclarationSyntax>()
            .Select(method => new ScriptMethodSummary(
                method.Identifier.ValueText,
                method.ParameterList.Parameters
                    .Select(parameter => new ScriptParameterSummary(
                        parameter.Identifier.ValueText,
                        parameter.Type is null
                            ? null
                            : GraphCSharpTypeNameResolver.Resolve(
                                parameter.Type,
                                semanticModel,
                                parameter.Type.ToString())))
                    .ToArray(),
                BuildBody(method)))
            .ToArray();
    }

    private static IReadOnlyList<ScriptBodyNodeSummary> BuildBody(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
        {
            return Array.Empty<ScriptBodyNodeSummary>();
        }

        return BuildStatementList(method.Body.Statements);
    }

    private static IReadOnlyList<ScriptBodyNodeSummary> BuildStatementList(SyntaxList<StatementSyntax> statements)
    {
        return statements.SelectMany(BuildStatement).ToArray();
    }

    private static IEnumerable<ScriptBodyNodeSummary> BuildStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case BlockSyntax block:
                return BuildStatementList(block.Statements);

            case IfStatementSyntax ifStatement:
                return BuildIfStatement(ifStatement);

            case WhileStatementSyntax whileStatement:
                return BuildWhileStatement(whileStatement);

            case ForStatementSyntax forStatement:
                return BuildForStatement(forStatement);

            case ForEachStatementSyntax forEachStatement:
                return BuildForEachStatement(forEachStatement);

            case DoStatementSyntax doStatement:
                return BuildDoStatement(doStatement);

            case LocalDeclarationStatementSyntax localDeclaration:
                return new[]
                {
                    new ScriptBodyNodeSummary("Local", localDeclaration.Declaration.ToString())
                };

            case ExpressionStatementSyntax expressionStatement:
                return new[] { BuildExpressionStatement(expressionStatement.Expression) };

            case ReturnStatementSyntax returnStatement:
                return new[]
                {
                    new ScriptBodyNodeSummary("Return", returnStatement.Expression?.ToString() ?? string.Empty)
                };

            default:
                return new[]
                {
                    new ScriptBodyNodeSummary(statement.Kind().ToString(), statement.ToString())
                };
        }
    }

    private static IEnumerable<ScriptBodyNodeSummary> BuildIfStatement(IfStatementSyntax ifStatement)
    {
        var children = BuildNestedStatement(ifStatement.Statement).ToList();

        if (ifStatement.Else is not null)
        {
            children.Add(new ScriptBodyNodeSummary(
                "Else",
                string.Empty,
                BuildNestedStatement(ifStatement.Else.Statement)));
        }

        return new[]
        {
            new ScriptBodyNodeSummary("If", ifStatement.Condition.ToString(), children)
        };
    }

    private static IEnumerable<ScriptBodyNodeSummary> BuildWhileStatement(WhileStatementSyntax whileStatement)
    {
        return new[]
        {
            new ScriptBodyNodeSummary(
                "While",
                whileStatement.Condition.ToString(),
                BuildNestedStatement(whileStatement.Statement))
        };
    }

    private static IEnumerable<ScriptBodyNodeSummary> BuildForStatement(ForStatementSyntax forStatement)
    {
        return new[]
        {
            new ScriptBodyNodeSummary(
                "For",
                forStatement.Condition?.ToString() ?? string.Empty,
                BuildNestedStatement(forStatement.Statement))
        };
    }

    private static IEnumerable<ScriptBodyNodeSummary> BuildForEachStatement(ForEachStatementSyntax forEachStatement)
    {
        return new[]
        {
            new ScriptBodyNodeSummary(
                "ForEach",
                $"{forEachStatement.Identifier.ValueText} in {forEachStatement.Expression}",
                BuildNestedStatement(forEachStatement.Statement))
        };
    }

    private static IEnumerable<ScriptBodyNodeSummary> BuildDoStatement(DoStatementSyntax doStatement)
    {
        return new[]
        {
            new ScriptBodyNodeSummary(
                "Do",
                doStatement.Condition.ToString(),
                BuildNestedStatement(doStatement.Statement))
        };
    }

    private static IReadOnlyList<ScriptBodyNodeSummary> BuildNestedStatement(StatementSyntax statement)
    {
        return statement is BlockSyntax block
            ? BuildStatementList(block.Statements)
            : BuildStatement(statement).ToArray();
    }

    private static ScriptBodyNodeSummary BuildExpressionStatement(ExpressionSyntax expression)
    {
        return expression switch
        {
            InvocationExpressionSyntax invocation => new ScriptBodyNodeSummary("Call", invocation.ToString()),
            AssignmentExpressionSyntax assignment => new ScriptBodyNodeSummary("Assign", assignment.ToString()),
            _ => new ScriptBodyNodeSummary("Expression", expression.ToString())
        };
    }

    private static bool HasBehaviorAttribute(ClassDeclarationSyntax classDeclaration)
    {
        return HasAttribute(classDeclaration.AttributeLists, "Behavior");
    }

    private static bool DerivesFromBehaviorComponent(ClassDeclarationSyntax classDeclaration)
    {
        return classDeclaration.BaseList?.Types.Any(type => type.Type.ToString() == "BehaviorComponent") == true;
    }

    private static string GetDefaultBehaviorId(ClassDeclarationSyntax behaviorClass)
    {
        var namespaceName = GetNamespaceName(behaviorClass);
        return string.IsNullOrEmpty(namespaceName)
            ? behaviorClass.Identifier.ValueText
            : $"{namespaceName}.{behaviorClass.Identifier.ValueText}";
    }

    private static string GetNamespaceName(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    return namespaceDeclaration.Name.ToString();

                case CompilationUnitSyntax:
                    return string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool IsBehaviorField(FieldDeclarationSyntax field)
    {
        return IsPublicInstanceField(field) || HasExplicitFieldAttribute(field.AttributeLists);
    }

    private static bool IsPublicInstanceField(FieldDeclarationSyntax field)
    {
        return field.Modifiers.Any(SyntaxKind.PublicKeyword)
            && !field.Modifiers.Any(SyntaxKind.StaticKeyword)
            && !field.Modifiers.Any(SyntaxKind.ConstKeyword);
    }

    private static bool HasExplicitFieldAttribute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return HasAttribute(attributeLists, "Field") || HasAttribute(attributeLists, "SerializeField");
    }

    private static bool TryGetStableFieldId(SyntaxList<AttributeListSyntax> attributeLists, out FieldId fieldId)
    {
        fieldId = default;
        var fieldAttribute = attributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(attribute => AttributeMatches(attribute, "Field"));
        var expression = fieldAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.NumericLiteralExpression) &&
            literal.Token.Value is int id)
        {
            fieldId = new FieldId(id);
            return true;
        }

        return false;
    }

    private static string GetAccessibility(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return "public";
        }

        if (modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return "protected";
        }

        if (modifiers.Any(SyntaxKind.InternalKeyword))
        {
            return "internal";
        }

        return "private";
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => AttributeMatches(attribute, attributeName));
    }

    private static string? GetFirstAttributeArgument(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        return GetAttributeArguments(attributeLists, attributeName).FirstOrDefault();
    }

    private static IReadOnlyList<string> GetAttributeArguments(
        SyntaxList<AttributeListSyntax> attributeLists,
        string attributeName)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .Where(attribute => AttributeMatches(attribute, attributeName))
            .Select(attribute => attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression)
            .OfType<ExpressionSyntax>()
            .Select(GetAttributeArgumentValue)
            .ToArray();
    }

    private static string GetAttributeArgumentValue(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal && literal.Token.ValueText.Length > 0
            ? literal.Token.ValueText
            : expression.ToString();
    }

    private static bool AttributeMatches(AttributeSyntax attribute, string expectedName)
    {
        var actualName = GetSimpleAttributeName(attribute.Name);
        return actualName == expectedName || actualName == $"{expectedName}Attribute";
    }

    private static string GetSimpleAttributeName(NameSyntax name)
    {
        return name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            _ => name.ToString()
        };
    }
}

public sealed record ScriptParseResult(
    string Path,
    IReadOnlyList<ScriptDiagnostic> Diagnostics,
    ScriptBehaviorSummary? Behavior)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == "Error");

    public bool HasSyntaxErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == "Error" && diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
}

public sealed record ScriptDiagnostic(
    string Id,
    string Stage,
    string Severity,
    string Message,
    string FileName,
    int Line,
    int Column);

public sealed record ScriptBehaviorSummary(
    string Name,
    string Id,
    string IdSource,
    IReadOnlyList<string> FormerlyBehaviorIds,
    IReadOnlyList<ScriptFieldSummary> Fields,
    IReadOnlyList<ScriptMethodSummary> Methods);

public sealed record ScriptFieldSummary(
    string Name,
    FieldId? FieldId,
    string Type,
    string Accessibility,
    string Serialization);

public sealed record ScriptMethodSummary(
    string Name,
    IReadOnlyList<ScriptParameterSummary> Parameters,
    IReadOnlyList<ScriptBodyNodeSummary> Body)
{
    public string Signature
    {
        get
        {
            var parameters = string.Join(
                ", ",
                Parameters.Select(parameter =>
                    parameter.Type is null
                        ? parameter.Name
                        : $"{parameter.Type} {parameter.Name}"));

            return $"{Name}({parameters})";
        }
    }
}

public sealed record ScriptParameterSummary(string Name, string? Type);

public sealed record ScriptBodyNodeSummary(
    string Kind,
    string Text,
    IReadOnlyList<ScriptBodyNodeSummary> Children)
{
    public ScriptBodyNodeSummary(string kind, string text)
        : this(kind, text, Array.Empty<ScriptBodyNodeSummary>())
    {
    }
}
