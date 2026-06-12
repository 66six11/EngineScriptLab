using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ScriptLab.GraphCSharp;

namespace ScriptLab;

internal static class GraphCSharpTypeNameResolver
{
    public static string Resolve(TypeSyntax? typeSyntax, SemanticModel? semanticModel, string fallback)
    {
        if (typeSyntax is null)
        {
            return fallback;
        }

        if (semanticModel is not null)
        {
            var type = semanticModel.GetTypeInfo(typeSyntax).Type;
            if (type is not null)
            {
                return Canonicalize(type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            }
        }

        return Canonicalize(typeSyntax.ToString());
    }

    public static string ResolveLocal(
        VariableDeclaratorSyntax variable,
        TypeSyntax declarationType,
        SemanticModel semanticModel)
    {
        if (semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol local)
        {
            return Canonicalize(local.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        }

        return Resolve(declarationType, semanticModel, declarationType.ToString());
    }

    private static string Canonicalize(string typeName)
    {
        if (GraphCSharpRuleSet.TryGetSupportedFieldTypeName(
                typeName,
                requireQualifiedMatch: true,
                out var supportedFieldType))
        {
            return supportedFieldType;
        }

        if (GraphCSharpRuleSet.TryGetSupportedFieldTypeName(
                typeName,
                requireQualifiedMatch: false,
                out supportedFieldType))
        {
            return supportedFieldType;
        }

        return NormalizeTypeName(typeName);
    }

    private static string NormalizeTypeName(string typeName)
    {
        const string globalPrefix = "global::";
        var normalized = typeName.Trim();
        return normalized.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? normalized.Substring(globalPrefix.Length)
            : normalized;
    }
}
