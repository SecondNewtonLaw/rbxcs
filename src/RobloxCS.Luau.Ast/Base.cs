namespace RobloxCS.Luau;

// Luau AST. C# OOP is *lowered* into these Luau primitives + runtime helper calls (decision #3);
// there is no dedicated class node. Add nodes when a transpiled construct needs them.
//
// Files: Base / Statements / ControlFlow / Functions / Expressions / Operators / Literals.

public abstract class LuauNode;

// A sequence of statements (function body, loop body, module top-level).
public sealed class Chunk : LuauNode
{
    public List<Statement> Statements { get; } = new();
}

public abstract class Statement : LuauNode;

public abstract class Expression : LuauNode;
