namespace RobloxCS.Luau;

// Loop and branch statements.

// if <c> then .. [elseif <c> then ..] [else ..] end
public sealed class IfStatement(IReadOnlyList<IfBranch> branches, Chunk? elseBody) : Statement
{
    public IReadOnlyList<IfBranch> Branches { get; } = branches;
    public Chunk? ElseBody { get; } = elseBody;
}

public sealed class IfBranch(Expression condition, Chunk body)
{
    public Expression Condition { get; } = condition;
    public Chunk Body { get; } = body;
}

// while <c> do .. end
public sealed class WhileStatement(Expression condition, Chunk body) : Statement
{
    public Expression Condition { get; } = condition;
    public Chunk Body { get; } = body;
}

// repeat .. until <c>   (used for C# do-while)
public sealed class RepeatStatement(Chunk body, Expression until) : Statement
{
    public Chunk Body { get; } = body;
    public Expression Until { get; } = until;
}

// for <var> = <start>, <stop>[, <step>] do .. end   (inclusive bounds)
public sealed class NumericFor(string variable, Expression start, Expression stop, Expression? step, Chunk body) : Statement
{
    public string Variable { get; } = variable;
    public Expression Start { get; } = start;
    public Expression Stop { get; } = stop;
    public Expression? Step { get; } = step;
    public Chunk Body { get; } = body;
}

// for <vars...> in <iter> do .. end   (generalized iteration)
public sealed class GenericFor(IReadOnlyList<string> variables, Expression iter, Chunk body) : Statement
{
    public IReadOnlyList<string> Variables { get; } = variables;
    public Expression Iter { get; } = iter;
    public Chunk Body { get; } = body;
}

// do .. end  (explicit scope; also wraps desugared non-canonical for loops)
public sealed class DoBlock(Chunk body) : Statement
{
    public Chunk Body { get; } = body;
}
