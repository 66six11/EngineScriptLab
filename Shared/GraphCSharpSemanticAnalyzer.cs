using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ScriptLab.GraphCSharp;

public static class GraphCSharpSemanticAnalyzer
{
    public static IReadOnlyList<GraphCSharpRestrictionDiagnostic> AnalyzeBehaviorFieldDeclaration(
        FieldDeclarationSyntax fieldDeclaration,
        SemanticModel? semanticModel)
    {
        var diagnostics = new List<GraphCSharpRestrictionDiagnostic>();
        var typeName = GetFieldTypeName(fieldDeclaration, semanticModel, out var isSymbolName);
        var isSupported = isSymbolName
            ? GraphCSharpRuleSet.IsSupportedFieldTypeSymbol(typeName)
            : GraphCSharpRuleSet.IsSupportedFieldType(typeName);

        if (isSupported)
        {
            return diagnostics;
        }

        foreach (var variable in fieldDeclaration.Declaration.Variables)
        {
            diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
                variable,
                GraphCSharpRuleSet.UnsupportedTypeId,
                GraphCSharpRuleSet.GetUnsupportedTypeMessage(typeName),
                GraphCSharpAnalysisStage.TypeCheck));
        }

        return diagnostics;
    }

    public static IReadOnlyList<GraphCSharpRestrictionDiagnostic> AnalyzeInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        GraphCSharpFunctionBinding binding)
    {
        var diagnostics = new List<GraphCSharpRestrictionDiagnostic>();

        AnalyzeFunctionContext(invocation, binding, diagnostics);
        AnalyzeFunctionSideEffects(invocation, binding, diagnostics);
        AnalyzeFunctionArguments(invocation, semanticModel, binding, diagnostics);

        return diagnostics;
    }

    private static string GetFieldTypeName(
        FieldDeclarationSyntax fieldDeclaration,
        SemanticModel? semanticModel,
        out bool isSymbolName)
    {
        if (semanticModel is not null)
        {
            var type = semanticModel.GetTypeInfo(fieldDeclaration.Declaration.Type).Type;
            if (type is not null)
            {
                isSymbolName = true;
                return type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            }
        }

        isSymbolName = false;
        return fieldDeclaration.Declaration.Type.ToString();
    }

    private static void AnalyzeFunctionContext(
        InvocationExpressionSyntax invocation,
        GraphCSharpFunctionBinding binding,
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        var context = GetScriptContext(invocation);
        if ((binding.AllowedContexts & context) != GraphCSharpScriptContextMask.None)
        {
            return;
        }

        diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
            invocation,
            GraphCSharpRuleSet.IllegalContextCallId,
            GraphCSharpRuleSet.GetIllegalContextCallMessage(
                binding.CSharpName,
                GetScriptContextName(context)),
            GraphCSharpAnalysisStage.ContextCheck));
    }

    private static void AnalyzeFunctionSideEffects(
        InvocationExpressionSyntax invocation,
        GraphCSharpFunctionBinding binding,
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        if ((binding.Effects & GraphCSharpEffectFlags.MutatesWorld) == GraphCSharpEffectFlags.None ||
            invocation.Parent is ExpressionStatementSyntax)
        {
            return;
        }

        diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
            invocation,
            GraphCSharpRuleSet.HiddenSideEffectId,
            GraphCSharpRuleSet.GetHiddenSideEffectMessage(binding.CSharpName),
            GraphCSharpAnalysisStage.EffectCheck));
    }

    private static void AnalyzeFunctionArguments(
        InvocationExpressionSyntax invocation,
        SemanticModel? semanticModel,
        GraphCSharpFunctionBinding binding,
        ICollection<GraphCSharpRestrictionDiagnostic> diagnostics)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != binding.Parameters.Count)
        {
            diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
                invocation,
                GraphCSharpRuleSet.UnsupportedTypeId,
                GraphCSharpRuleSet.GetInvalidFunctionArgumentCountMessage(
                    binding.CSharpName,
                    binding.Parameters.Count,
                    arguments.Count),
                GraphCSharpAnalysisStage.TypeCheck));
            return;
        }

        if (semanticModel is null)
        {
            return;
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            var expectedType = binding.Parameters[index].TypeName;
            if (expectedType == "*")
            {
                continue;
            }

            var actualType = semanticModel.GetTypeInfo(arguments[index].Expression).Type;
            if (actualType is null)
            {
                continue;
            }

            var actualTypeName = actualType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (TypeMatches(expectedType, actualTypeName))
            {
                continue;
            }

            diagnostics.Add(new GraphCSharpRestrictionDiagnostic(
                arguments[index].Expression,
                GraphCSharpRuleSet.UnsupportedTypeId,
                GraphCSharpRuleSet.GetInvalidFunctionArgumentTypeMessage(
                    binding.CSharpName,
                    index + 1,
                    expectedType,
                    actualTypeName),
                GraphCSharpAnalysisStage.TypeCheck));
        }
    }

    private static bool TypeMatches(string expectedType, string actualType)
    {
        return string.Equals(expectedType, actualType, StringComparison.Ordinal) ||
            string.Equals($"Asharia.Behavior.{expectedType}", NormalizeTypeName(actualType), StringComparison.Ordinal);
    }

    private static string NormalizeTypeName(string typeName)
    {
        const string globalPrefix = "global::";
        var normalized = typeName.Trim();
        return normalized.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? normalized.Substring(globalPrefix.Length)
            : normalized;
    }

    private static GraphCSharpScriptContextMask GetScriptContext(SyntaxNode node)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return method?.Identifier.ValueText switch
        {
            "Start" => GraphCSharpScriptContextMask.Start,
            "Update" => GraphCSharpScriptContextMask.Update,
            "FixedUpdate" => GraphCSharpScriptContextMask.FixedUpdate,
            "Destroy" => GraphCSharpScriptContextMask.Destroy,
            _ => GraphCSharpScriptContextMask.None
        };
    }

    private static string GetScriptContextName(GraphCSharpScriptContextMask context)
    {
        return context switch
        {
            GraphCSharpScriptContextMask.Start => "Start",
            GraphCSharpScriptContextMask.Update => "Update",
            GraphCSharpScriptContextMask.FixedUpdate => "FixedUpdate",
            GraphCSharpScriptContextMask.Destroy => "Destroy",
            _ => "unknown"
        };
    }
}
