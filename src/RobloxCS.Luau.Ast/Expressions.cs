namespace RobloxCS.Luau;

// Core value expressions: names, access, calls. Operators live in Operators.cs; literals and
// table/string data in Literals.cs.

public sealed class Identifier(string name) : Expression
{
    public string Name { get; } = name;
}

// `<target>.<member>`
public sealed class MemberAccess(Expression target, string member) : Expression
{
    public Expression Target { get; } = target;
    public string Member { get; } = member;
}

// `<target>[<index>]` — raw table indexing (also usable as an assignment lvalue).
public sealed class IndexAccess(Expression target, Expression index) : Expression
{
    public Expression Target { get; } = target;
    public Expression Index { get; } = index;
}

// `<callee>(args)`
public sealed class Call(Expression callee, IReadOnlyList<Expression> arguments) : Expression
{
    public Expression Callee { get; } = callee;
    public IReadOnlyList<Expression> Arguments { get; } = arguments;
}

// `<receiver>:<method>(args)` — colon call, implicit self. C# instance-method invocations lower here.
public sealed class MethodCall(Expression receiver, string method, IReadOnlyList<Expression> arguments) : Expression
{
    public Expression Receiver { get; } = receiver;
    public string Method { get; } = method;
    public IReadOnlyList<Expression> Arguments { get; } = arguments;
}

// Pre-rendered Luau expression text. Escape hatch (e.g. require paths, `nil`).
public sealed class RawExpression(string text) : Expression
{
    public string Text { get; } = text;
}
