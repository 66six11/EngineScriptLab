using ScriptLab.GraphCSharp;

namespace ScriptLab.Tests;

public sealed class GraphCSharpBindingRegistryTests
{
    [Fact]
    public void Functions_WhenRegistered_CarryStableMetadata()
    {
        Assert.All(GraphCSharpBindingRegistry.Functions, binding =>
        {
            Assert.NotEqual(default, binding.FunctionId);
            Assert.False(string.IsNullOrWhiteSpace(binding.CSharpQualifiedName));
            Assert.False(string.IsNullOrWhiteSpace(binding.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(binding.Category));
            Assert.True(binding.Version > 0);
        });

        Assert.Equal(
            GraphCSharpBindingRegistry.Functions.Length,
            GraphCSharpBindingRegistry.Functions.Select(binding => binding.FunctionId).Distinct().Count());
    }

    [Fact]
    public void TryGetFunctionBinding_WhenInputKeyDownIsRegistered_ReturnsStableMetadata()
    {
        var found = GraphCSharpBindingRegistry.TryGetFunctionBinding(
            "Input.KeyDown",
            out var binding);

        Assert.True(found);
        Assert.Equal(new FunctionId("asharia.input.keyDown"), binding.FunctionId);
        Assert.Equal("Key Down", binding.DisplayName);
        Assert.Equal("Input", binding.Category);
        Assert.Equal(1, binding.Version);
        Assert.Null(binding.ReplacedBy);
    }
}
