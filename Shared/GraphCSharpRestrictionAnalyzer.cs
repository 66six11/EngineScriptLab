using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ScriptLab.GraphCSharp;

public sealed class GraphCSharpRestrictionDiagnostic
{
    public GraphCSharpRestrictionDiagnostic(
        SyntaxNode node,
        string id,
        string message,
        string stage = GraphCSharpAnalysisStage.SyntaxRestriction)
    {
        Node = node;
        Id = id;
        Message = message;
        Stage = stage;
    }

    public SyntaxNode Node { get; }

    public string Id { get; }

    public string Message { get; }

    public string Stage { get; }
}

public static class GraphCSharpRestrictionAnalyzer
{
    private static readonly SyntaxKind[] CustomSyntaxKinds =
    {
        SyntaxKind.InvocationExpression,
        SyntaxKind.FieldDeclaration,
        SyntaxKind.ObjectCreationExpression,
        SyntaxKind.ImplicitObjectCreationExpression,
        SyntaxKind.AliasQualifiedName,
        SyntaxKind.ArrayType,
        SyntaxKind.GenericName,
        SyntaxKind.IdentifierName,
        SyntaxKind.NullableType,
        SyntaxKind.PointerType,
        SyntaxKind.PredefinedType,
        SyntaxKind.QualifiedName
    };

    public static SyntaxKind[] SyntaxKinds { get; } = GraphCSharpRuleSet.SyntaxKinds
        .Concat(CustomSyntaxKinds)
        .Distinct()
        .ToArray();

    public static IReadOnlyList<GraphCSharpRestrictionDiagnostic> AnalyzeNode(
        SyntaxNode node,
        SemanticModel? semanticModel = null)
    {
        var diagnostics = new List<GraphCSharpRestrictionDiagnostic>();

        var rule = GraphCSharpRuleSet.Find(node.Kind());
        if (rule is not null)
        {
            diagnostics.Add(new GraphCSharpRestrictionDiagnostic(node, rule.Id, rule.Message));
        }

        switch (node)
        {
            case InvocationExpressionSyntax invocation:
                AnalyzeInvocation(invocation, semanticModel, diagnostics);
                break;

            case TypeSyntax type:
                AnalyzeType(type, diagnostics);
                break;

            case FieldDeclarationSyntax fieldDeclaration:
                AnalyzeFieldDeclaration(fieldDeclaration, semanticModel, diagnostics);
                break;

            case ObjectCreationExpressionSyntax objectCreation:
                AnalyzeObjectCreation(objectCreation, semanticModel, diagnostics);
                break;

            case ImplicitObjectCreationExpressionSyntax implicitObjectCreation:
                diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
                    implicitObjectCreation,
                    GraphCSharpRuleSet.UnsupportedAllocationId,
                    GraphCSharpRuleSet.GetUnsupportedImplicitAllocationMessage()));
                break;
        }

        return diagnostics;
    }

    private static void AnalyzeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            IsReflectionGetCall(memberAccess))
        {
            diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
                invocation,
                GraphCSharpRuleSet.UnsupportedExpressionId,
                GraphCSharpRuleSet.GetUnsupportedReflectionMessage(memberAccess.ToString()),
                GraphCSharpAnalysisStage.SyntaxRestriction));
            return;
        }

        if (GraphCSharpBindingRegistry.TryResolveFunctionBinding(
                invocation,
                semanticModel,
                out var binding,
                out var csharpName))
        {
            foreach (var diagnostic in GraphCSharpSemanticAnalyzer.AnalyzeInvocation(
                         invocation,
                         semanticModel,
                         binding))
            {
                diagnostics.Add(diagnostic);
            }

            return;
        }

        diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
            invocation,
            GraphCSharpRuleSet.UnregisteredFunctionCallId,
            GraphCSharpBindingRegistry.GetUnregisteredFunctionCallMessage(csharpName),
            GraphCSharpAnalysisStage.SemanticBinding));
    }

    private static void AnalyzeType(
        TypeSyntax type,
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        if (type.Parent is TypeSyntax)
        {
            return;
        }

        var typeName = GetTypeName(type);
        if (!GraphCSharpRuleSet.IsUnsupportedType(typeName))
        {
            return;
        }

        diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
            type,
            GraphCSharpRuleSet.UnsupportedTypeId,
            GraphCSharpRuleSet.GetUnsupportedTypeMessage(typeName),
            GraphCSharpAnalysisStage.TypeCheck));
    }

    private static void AnalyzeFieldDeclaration(
        FieldDeclarationSyntax fieldDeclaration,
        SemanticModel? semanticModel,
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        if (fieldDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword) &&
            !fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            foreach (var variable in fieldDeclaration.Declaration.Variables)
            {
                diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
                    variable,
                    GraphCSharpRuleSet.UnsupportedSyntaxId,
                    GraphCSharpRuleSet.GetUnsupportedStaticStateMessage(variable.Identifier.ValueText),
                    GraphCSharpAnalysisStage.SyntaxRestriction));
            }
        }

        if (!IsBehaviorField(fieldDeclaration))
        {
            return;
        }

        if (TryGetStableFieldId(fieldDeclaration.AttributeLists, out _))
        {
            foreach (var diagnostic in GraphCSharpSemanticAnalyzer.AnalyzeBehaviorFieldDeclaration(
                         fieldDeclaration,
                         semanticModel))
            {
                diagnostics.Add(diagnostic);
            }

            return;
        }

        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
                variable,
                GraphCSharpRuleSet.MissingStableFieldId,
                GraphCSharpRuleSet.GetMissingStableFieldIdMessage(variable.Identifier.ValueText),
                GraphCSharpAnalysisStage.SemanticBinding));
        }

        foreach (var diagnostic in GraphCSharpSemanticAnalyzer.AnalyzeBehaviorFieldDeclaration(
                     fieldDeclaration,
                     semanticModel))
        {
            diagnostics.Add(diagnostic);
        }
    }

    private static void AnalyzeObjectCreation(
        ObjectCreationExpressionSyntax objectCreation,
        SemanticModel? semanticModel,
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        var typeName = GetObjectCreationTypeName(objectCreation, semanticModel, out var isSymbolName);
        if (isSymbolName
                ? GraphCSharpRuleSet.IsConstructibleValueTypeSymbol(typeName)
                : GraphCSharpRuleSet.IsConstructibleValueType(typeName))
        {
            return;
        }

        diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
            objectCreation,
            GraphCSharpRuleSet.UnsupportedAllocationId,
            GraphCSharpRuleSet.GetUnsupportedAllocationMessage(typeName),
            GraphCSharpAnalysisStage.SyntaxRestriction));
    }

    private static string GetObjectCreationTypeName(
        ObjectCreationExpressionSyntax objectCreation,
        SemanticModel? semanticModel,
        out bool isSymbolName)
    {
        if (semanticModel is not null)
        {
            var type = semanticModel.GetTypeInfo(objectCreation).Type;
            if (type is not null)
            {
                isSymbolName = true;
                return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            }
        }

        isSymbolName = false;
        return GetTypeName(objectCreation.Type);
    }

    private static bool IsReflectionGetCall(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Expression is TypeOfExpressionSyntax &&
            memberAccess.Name.Identifier.ValueText.StartsWith("Get", StringComparison.Ordinal);
    }

    private static bool IsBehaviorField(FieldDeclarationSyntax field)
    {
        return IsPublicInstanceField(field) ||
            HasAttribute(field.AttributeLists, "Field") ||
            HasAttribute(field.AttributeLists, "SerializeField");
    }

    private static bool IsPublicInstanceField(FieldDeclarationSyntax field)
    {
        return field.Modifiers.Any(SyntaxKind.PublicKeyword) &&
            !field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
            !field.Modifiers.Any(SyntaxKind.ConstKeyword);
    }

    private static bool TryGetStableFieldId(SyntaxList<AttributeListSyntax> attributeLists, out int fieldId)
    {
        fieldId = 0;
        var fieldAttribute = attributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(attribute => AttributeMatches(attribute, "Field"));
        var expression = fieldAttribute?.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.NumericLiteralExpression) &&
            literal.Token.Value is int id)
        {
            fieldId = id;
            return true;
        }

        return false;
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => AttributeMatches(attribute, attributeName));
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

    private static string GetTypeName(TypeSyntax type)
    {
        switch (type)
        {
            case AliasQualifiedNameSyntax aliasQualifiedName:
                return GetTypeName(aliasQualifiedName.Name);
            case ArrayTypeSyntax arrayType:
                return GetTypeName(arrayType.ElementType);
            case GenericNameSyntax genericName:
                return genericName.Identifier.ValueText;
            case IdentifierNameSyntax identifierName:
                return identifierName.Identifier.ValueText;
            case NullableTypeSyntax nullableType:
                return GetTypeName(nullableType.ElementType);
            case PointerTypeSyntax pointerType:
                return GetTypeName(pointerType.ElementType);
            case PredefinedTypeSyntax predefinedType:
                return predefinedType.Keyword.ValueText;
            case QualifiedNameSyntax qualifiedName:
                return GetTypeName(qualifiedName.Right);
            default:
                return type.ToString();
        }
    }
}
