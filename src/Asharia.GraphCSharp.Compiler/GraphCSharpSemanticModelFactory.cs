using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ScriptLab;

internal static class GraphCSharpSemanticModelFactory
{
    private static readonly Lazy<SyntaxTree> WellKnownApiTree = new(() =>
        CSharpSyntaxTree.ParseText(
            WellKnownBehaviorApiSource,
            path: "__GraphCSharpWellKnownBehaviorApi.g.cs"));

    private static readonly Lazy<MetadataReference[]> References = new(() =>
        new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        });

    public static SemanticModel CreateSemanticModel(SyntaxTree tree)
    {
        var compilation = CSharpCompilation.Create(
            "GraphCSharpSemanticAnalysis",
            new[] { tree, WellKnownApiTree.Value },
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetSemanticModel(tree, ignoreAccessibility: true);
    }

    private const string WellKnownBehaviorApiSource = """
        using System;

        namespace Asharia.Behavior
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class BehaviorAttribute : Attribute
            {
                public BehaviorAttribute(string id) {}
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public sealed class FormerlyBehaviorAttribute : Attribute
            {
                public FormerlyBehaviorAttribute(string id) {}
            }

            [AttributeUsage(AttributeTargets.Field)]
            public sealed class FieldAttribute : Attribute
            {
                public FieldAttribute() {}
                public FieldAttribute(int id) {}
            }

            [AttributeUsage(AttributeTargets.Field)]
            public sealed class ExposeAttribute : Attribute {}

            [AttributeUsage(AttributeTargets.Field)]
            public sealed class SerializeFieldAttribute : Attribute {}

            [AttributeUsage(AttributeTargets.Field)]
            public sealed class RangeAttribute : Attribute
            {
                public RangeAttribute(float min, float max) {}
            }

            public abstract class BehaviorComponent
            {
                protected EntityRef Self => default;
                protected virtual void Start() {}
                protected virtual void Update(float delta) {}
                protected virtual void FixedUpdate(float delta) {}
                protected virtual void Destroy() {}
            }

            public readonly struct EntityRef
            {
                public EntityRef(int id) {}
            }

            public readonly struct Vec3
            {
                public Vec3(float x, float y, float z) {}
            }

            public enum Key
            {
                W,
                A,
                S,
                D
            }

            public static class Input
            {
                public static bool KeyDown(Key key) => false;
            }

            public static class Transform
            {
                public static void Translate(EntityRef entity, Vec3 offset) {}
            }

            public static class GraphDebug
            {
                public static T Inspect<T>(string name, T value) => value;
                public static void Watch<T>(string name, T value) {}
            }
        }
        """;
}
