namespace RobloxCS.Luau;

// Operator and grouping expressions.

// `<left> <op> <right>` (op already mapped to Luau: ==, ~=, and, or, .., + …).
public sealed class Binary(Expression left, string op, Expression right) : Expression
{
    public Expression Left { get; } = left;
    public string Op { get; } = op;
    public Expression Right { get; } = right;
}

// prefix unary: `not x`, `-x`, `#x`
public sealed class Unary(string op, Expression operand) : Expression
{
    public string Op { get; } = op;
    public Expression Operand { get; } = operand;
}

// `(inner)` — explicit grouping to keep precedence when lowering (e.g. `not (a == b)`).
public sealed class Paren(Expression inner) : Expression
{
    public Expression Inner { get; } = inner;
}

// value-if: `if <c> then <a> else <b>` (C# ternary / switch expression)
public sealed class IfExpression(Expression condition, Expression thenExpr, Expression elseExpr) : Expression
{
    public Expression Condition { get; } = condition;
    public Expression Then { get; } = thenExpr;
    public Expression Else { get; } = elseExpr;
}
