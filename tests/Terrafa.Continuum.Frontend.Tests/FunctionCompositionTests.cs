using Terrafa.Continuum.Frontend.Models;

namespace Terrafa.Continuum.Frontend.Tests;

[Collection("function library")]
public class FunctionCompositionTests
{
    private static readonly FunctionLibrary Library = FunctionLibrary.Instance;

    private static LibraryFunction Fn(string name) =>
        Library.Find(name) ?? throw new InvalidOperationException($"{name} missing from library");

    [Fact]
    public void FloorIsUnaryWithOneScalarInput()
    {
        var floor = Fn("floor");
        Assert.Equal([new FunctionPort("u", PortKind.Scalar)], floor.Inputs);
        Assert.True(floor.IsUnaryScalar);
        Assert.Equal("UNARY", floor.ArityLabel);
        Assert.Equal(2.0, floor.Apply([2.7]));
        Assert.Equal(-3.0, floor.ApplyUnary(-2.3));
    }

    [Fact]
    public void AddIsBinaryWithTwoScalarInputs()
    {
        var add = Fn("add");
        Assert.Equal(2, add.Inputs.Count);
        Assert.All(add.Inputs, port => Assert.Equal(PortKind.Scalar, port.Kind));
        Assert.False(add.IsUnaryScalar);
        Assert.Equal("BINARY", add.ArityLabel);
        Assert.Equal(5.0, add.Apply([2.0, 3.0]));
    }

    [Fact]
    public void MaxTakesASingleArrayInput()
    {
        var max = Fn("max");
        Assert.Equal([new FunctionPort("u", PortKind.Array)], max.Inputs);
        Assert.True(max.HasArrayInput);
        Assert.False(max.IsUnaryScalar);
        Assert.Equal("AGGREGATE", max.ArityLabel);
        Assert.Equal(4.0, max.Apply([1.0, 4.0, 2.0]));
    }

    [Fact]
    public void ApplyUnaryRefusesNonUnaryFunctions()
    {
        Assert.Throws<InvalidOperationException>(() => Fn("add").ApplyUnary(1.0));
        Assert.Throws<InvalidOperationException>(() => Fn("max").ApplyUnary(1.0));
    }

    [Fact]
    public void TreeEvaluatesBranchesIndependently()
    {
        var tree = new FunctionNode(Fn("add"),
        [
            new FunctionNode(Fn("floor"), [new VariableNode()]),
            new FunctionNode(Fn("max"), [new VariableNode(), new FunctionNode(Fn("exp"), [new VariableNode()])])
        ]);

        var x = 1.5;
        var expected = Math.Floor(x) + Math.Max(x, Math.Exp(x));
        Assert.Equal(expected, tree.Evaluate(x), 12);
        Assert.Equal("⌊x⌋ + max(x, exp(x))", tree.Formula("x"));
        Assert.Equal(4, tree.CountFunctionNodes());
        Assert.Equal(3, tree.Depth());
    }

    [Fact]
    public void ConstantsFeedScalarPorts()
    {
        var tree = new FunctionNode(Fn("multiply"),
        [
            new ConstantNode(2.0),
            new FunctionNode(Fn("add"), [new VariableNode(), new ConstantNode(1.0)])
        ]);

        Assert.Equal(2.0 * (3.0 + 1.0), tree.Evaluate(3.0));
        Assert.Equal("2·(x + 1)", tree.Formula("x"));
        Assert.True(tree.ContainsVariable());
    }

    [Fact]
    public void CreateWrapsExistingSubtreeAsFirstArgument()
    {
        var existing = new FunctionNode(Fn("exp"), [new VariableNode()]);

        var wrappedInAdd = FunctionNode.Create(Fn("add"), existing);
        Assert.Same(existing, wrappedInAdd.Arguments[0]);
        Assert.Equal(2, wrappedInAdd.Arguments.Count);
        Assert.IsType<VariableNode>(wrappedInAdd.Arguments[1]);
        Assert.Equal("exp(x) + x", wrappedInAdd.Formula("x"));

        var wrappedInMax = FunctionNode.Create(Fn("max"), existing);
        Assert.Equal(2, wrappedInMax.Arguments.Count);
        Assert.Equal("max(exp(x), x)", wrappedInMax.Formula("x"));
    }

    [Fact]
    public void AggregateArgumentsGrowAndValidate()
    {
        var max = Fn("max");
        var node = FunctionNode.Create(max, new VariableNode());
        Assert.True(node.CanAddArgument);
        Assert.True(node.CanRemoveArgument);
        node.Arguments.Add(new ConstantNode(0.5));
        Assert.Equal(0.5, node.Evaluate(0.1));
        Assert.Equal("u1", node.PortLabel(0));
        Assert.Equal("u3", node.PortLabel(2));

        Assert.Throws<ArgumentException>(() => new FunctionNode(max, []));
        Assert.Throws<ArgumentException>(() => new FunctionNode(Fn("add"), [new VariableNode()]));
    }

    [Fact]
    public void SavedCompositeIsAUnaryFunctionOfX()
    {
        var tree = new FunctionNode(Fn("add"),
        [
            new FunctionNode(Fn("square"), [new VariableNode()]),
            new ConstantNode(1.0)
        ]);
        var composite = Library.SaveComposite("test_sq_plus_one", tree);

        Assert.True(composite.IsUnaryScalar);
        Assert.Equal(10.0, composite.ApplyUnary(3.0));
        Assert.Equal("x² + 1", composite.DisplayFormula);
        Assert.Same(composite, Library.Find("test_sq_plus_one"));
    }

    [Fact]
    public void SavedCompositeIsIsolatedFromLaterDraftEdits()
    {
        var constant = new ConstantNode(1.0);
        var tree = new FunctionNode(Fn("add"), [new VariableNode(), constant]);
        var composite = Library.SaveComposite("test_isolated", tree);

        constant.Value = 100.0;
        tree.Arguments[0] = new ConstantNode(0.0);

        Assert.Equal(3.0, composite.ApplyUnary(2.0));
    }

    [Fact]
    public void CompositesNestInsideOtherTrees()
    {
        var inner = Library.SaveComposite("test_nested_inner",
            new FunctionNode(Fn("square"), [new VariableNode()]));
        var outer = new FunctionNode(Fn("add"),
        [
            new FunctionNode(inner, [new VariableNode()]),
            new VariableNode()
        ]);

        Assert.Equal(12.0, outer.Evaluate(3.0));
        Assert.Equal("test_nested_inner(x) + x", outer.Formula("x"));
    }

    [Fact]
    public void TransferFormulaStillRendersUnaryStages()
    {
        var formula = TransferMath.Formula(TransferCombiner.Sum, Fn("exp"), ["a", "b"]);
        Assert.Equal("exp(sum(a, b))", formula);
    }

    [Fact]
    public void TransferEvaluateCarriesSigmaThroughUnaryStage()
    {
        var input = new TransferInput("tank", 1.0, 0.1, "bbl", [1.0, 2.0], [0.1, 0.1]);
        var result = TransferMath.Evaluate(TransferCombiner.Sum, Fn("negate"), [input]);

        Assert.NotNull(result);
        Assert.Equal(-1.0, result.Value, 12);
        Assert.Equal(0.1, result.Sigma, 12);
        Assert.True(result.Linearised);
    }
}
