using System.Globalization;

namespace Terrafa.Continuum.Frontend.Models;

public abstract class CompositionNode
{
    public abstract double Evaluate(double x);

    public abstract string Formula(string variable);

    public abstract CompositionNode Clone();

    public int CountFunctionNodes() => this is FunctionNode node
        ? 1 + node.Arguments.Sum(argument => argument.CountFunctionNodes())
        : 0;

    public int Depth() => this is FunctionNode node
        ? 1 + node.Arguments.Max(argument => argument.Depth())
        : 0;

    public bool ContainsVariable() => this switch
    {
        VariableNode => true,
        FunctionNode node => node.Arguments.Any(argument => argument.ContainsVariable()),
        _ => false
    };
}

public sealed class VariableNode : CompositionNode
{
    public override double Evaluate(double x) => x;

    public override string Formula(string variable) => variable;

    public override CompositionNode Clone() => new VariableNode();
}

public sealed class ConstantNode : CompositionNode
{
    public double Value { get; set; }

    public ConstantNode(double value) => Value = value;

    public override double Evaluate(double x) => Value;

    public override string Formula(string variable) => Format(Value);

    public override CompositionNode Clone() => new ConstantNode(Value);

    public static string Format(double value) =>
        double.IsNaN(value) ? "?" : value.ToString("0.####", CultureInfo.InvariantCulture);
}

public sealed class FunctionNode : CompositionNode
{
    public LibraryFunction Function { get; }

    public List<CompositionNode> Arguments { get; }

    public FunctionNode(LibraryFunction function, IEnumerable<CompositionNode> arguments)
    {
        Function = function;
        Arguments = arguments.ToList();
        if (function.HasArrayInput)
        {
            if (Arguments.Count == 0)
                throw new ArgumentException($"{function.Name} needs at least one argument");
        }
        else if (Arguments.Count != function.Inputs.Count)
        {
            throw new ArgumentException(
                $"{function.Name} takes {function.Inputs.Count} argument(s), got {Arguments.Count}");
        }
    }

    public static FunctionNode Create(LibraryFunction function, CompositionNode first)
    {
        var slots = function.HasArrayInput ? 2 : function.Inputs.Count;
        var arguments = new List<CompositionNode> { first };
        while (arguments.Count < slots)
            arguments.Add(new VariableNode());
        return new FunctionNode(function, arguments);
    }

    public bool CanAddArgument => Function.HasArrayInput;

    public bool CanRemoveArgument => Function.HasArrayInput && Arguments.Count > 1;

    public string PortLabel(int index) => Function.HasArrayInput
        ? $"{Function.Inputs[0].Name}{index + 1}"
        : Function.Inputs[index].Name;

    public override double Evaluate(double x) =>
        Function.Apply(Arguments.Select(argument => argument.Evaluate(x)).ToArray());

    public override string Formula(string variable) =>
        Function.FormatApplied(Arguments.Select(argument => argument.Formula(variable)).ToArray());

    public override CompositionNode Clone() =>
        new FunctionNode(Function, Arguments.Select(argument => argument.Clone()));
}
