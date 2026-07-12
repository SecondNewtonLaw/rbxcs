namespace RobloxCS.Luau;

// Literal and aggregate-data expressions.

// A pre-rendered literal token: string with quotes, number text, `true`/`false`.
public sealed class Literal(string raw) : Expression
{
    public string Raw { get; } = raw;
}

// `{ k1 = v1, ... }` (named) or `{ v1, v2 }` (positional). Key null => positional.
public sealed class TableConstructor(IReadOnlyList<TableEntry> entries) : Expression
{
    public IReadOnlyList<TableEntry> Entries { get; } = entries;
}

public sealed class TableEntry(string? key, Expression value)
{
    public string? Key { get; } = key;
    public Expression Value { get; } = value;
}

// `text {expr} text` rendered as a Luau backtick interpolated string.
public sealed class InterpolatedString(IReadOnlyList<InterpolationPart> parts) : Expression
{
    public IReadOnlyList<InterpolationPart> Parts { get; } = parts;
}

// Text is a raw literal chunk (Expression null); or Expression is an interpolated hole (Text null).
public sealed class InterpolationPart(string? text, Expression? expression)
{
    public string? Text { get; } = text;
    public Expression? Expression { get; } = expression;
}
