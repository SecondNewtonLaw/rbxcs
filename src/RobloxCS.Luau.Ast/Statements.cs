namespace RobloxCS.Luau;

// Simple (non-control-flow) statements. Control flow lives in ControlFlow.cs; function
// definitions in Functions.cs.

// `local <name> = <value>` (value optional).
public sealed class LocalDeclaration(string name, Expression? value) : Statement
{
    public string Name { get; } = name;
    public Expression? Value { get; } = value;
}

// Several statements emitted inline at the same scope (no do/end wrapper) — e.g. one C# multi-declarator
// `int a = 1, b, c = 3;` -> three `local` lines.
public sealed class MultiStatement(System.Collections.Generic.IReadOnlyList<Statement> statements) : Statement
{
    public System.Collections.Generic.IReadOnlyList<Statement> Statements { get; } = statements;
}

// `<target> = <value>` (target is an lvalue: Identifier or MemberAccess).
public sealed class Assignment(Expression target, Expression value) : Statement
{
    public Expression Target { get; } = target;
    public Expression Value { get; } = value;
}

// `<target> <op>= <value>` — Luau native compound assignment (op is +, -, *, /, %, ^, ..).
public sealed class CompoundAssignment(Expression target, string op, Expression value) : Statement
{
    public Expression Target { get; } = target;
    public string Op { get; } = op;
    public Expression Value { get; } = value;
}

// A bare expression evaluated for effect (e.g. a call statement).
public sealed class ExpressionStatement(Expression expression) : Statement
{
    public Expression Expression { get; } = expression;
}

// `return [value]`.
public sealed class Return(Expression? value) : Statement
{
    public Expression? Value { get; } = value;
}

public sealed class Break : Statement;

public sealed class Continue : Statement;

// Pre-rendered Luau statement text. Escape hatch for constructs not yet modelled as nodes.
public sealed class RawStatement(string text) : Statement
{
    public string Text { get; } = text;
}
