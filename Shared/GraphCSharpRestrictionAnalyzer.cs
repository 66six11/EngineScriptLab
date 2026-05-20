using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ScriptLab.GraphCSharp;

public sealed class GraphCSharpRestrictionDiagnostic
{
    public GraphCSharpRestrictionDiagnostic(SyntaxNode node, string id, string message)
    {
        Node = node;
        Id = id;
        Message = message;
    }

    public SyntaxNode Node { get; }

    public string Id { get; }

    public string Message { get; }
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

    public static IReadOnlyList<GraphCSharpRestrictionDiagnostic> AnalyzeNode(SyntaxNode node)
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
                AnalyzeInvocation(invocation, diagnostics);
                break;

            case TypeSyntax type:
                AnalyzeType(type, diagnostics);
                break;

            case FieldDeclarationSyntax fieldDeclaration:
                AnalyzeFieldDeclaration(fieldDeclaration, diagnostics);
                break;

            case ObjectCreationExpressionSyntax objectCreation:
                AnalyzeObjectCreation(objectCreation, diagnostics);
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
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (IsReflectionGetCall(memberAccess))
        {
            diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
                invocation,
                GraphCSharpRuleSet.UnsupportedExpressionId,
                GraphCSharpRuleSet.GetUnsupportedReflectionMessage(memberAccess.ToString())));
            return;
        }

        var csharpName = memberAccess.ToString();
        if (GraphCSharpBindingRegistry.TryGetFunctionId(csharpName, out _))
        {
            return;
        }

        diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
            invocation,
            GraphCSharpRuleSet.UnregisteredFunctionCallId,
            GraphCSharpBindingRegistry.GetUnregisteredFunctionCallMessage(csharpName)));
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
            GraphCSharpRuleSet.GetUnsupportedTypeMessage(typeName)));
    }

    private static void AnalyzeFieldDeclaration(
        FieldDeclarationSyntax fieldDeclaration,
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        if (!fieldDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword) ||
            fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return;
        }

        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
                variable,
                GraphCSharpRuleSet.UnsupportedSyntaxId,
                GraphCSharpRuleSet.GetUnsupportedStaticStateMessage(variable.Identifier.ValueText)));
        }
    }

    private static void AnalyzeObjectCreation(
        ObjectCreationExpressionSyntax objectCreation,
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        var typeName = GetTypeName(objectCreation.Type);
        if (GraphCSharpRuleSet.IsConstructibleValueType(typeName))
        {
            return;
        }

        diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
            objectCreation,
            GraphCSharpRuleSet.UnsupportedAllocationId,
            GraphCSharpRuleSet.GetUnsupportedAllocationMessage(typeName)));
    }

    private static bool IsReflectionGetCall(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Expression is TypeOfExpressionSyntax &&
            memberAccess.Name.Identifier.ValueText.StartsWith("Get", StringComparison.Ordinal);
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
