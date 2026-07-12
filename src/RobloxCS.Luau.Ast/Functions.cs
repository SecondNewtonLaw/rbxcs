namespace RobloxCS.Luau;

// Function definitions (statement form and expression form).

// `[local] function <a.b.c>(params) body end`, or method form `function <a.b>:name(params)`.
// NameChain is the dotted path; when IsMethod the final segment binds with `:` (implicit self).
public sealed class FunctionStatement(
    IReadOnlyList<string> nameChain, bool isMethod, IReadOnlyList<string> parameters, Chunk body, bool local = false) : Statement
{
    public IReadOnlyList<string> NameChain { get; } = nameChain;
    public bool IsMethod { get; } = isMethod;
    public IReadOnlyList<string> Parameters { get; } = parameters;
    public Chunk Body { get; } = body;
    public bool Local { get; } = local;
}

// `function(params) body end` — anonymous function value.
public sealed class FunctionExpression(IReadOnlyList<string> parameters, Chunk body) : Expression
{
    public IReadOnlyList<string> Parameters { get; } = parameters;
    public Chunk Body { get; } = body;
}
