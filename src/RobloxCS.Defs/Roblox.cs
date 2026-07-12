namespace Roblox;

/// <summary>
/// Luau globals (not Roblox instance classes). <c>Instance</c>, services, enums and datatypes come
/// from the generated API surface instead.
/// </summary>
public static class Globals
{
    /// <summary>The <c>game</c> DataModel root.</summary>
    public static readonly DataModel game = default!;

    /// <summary>The <c>workspace</c> service.</summary>
    public static readonly Workspace workspace = default!;

    /// <summary>The running script instance (for <c>script:GetActor()</c>, etc.).</summary>
    public static readonly Instance script = default!;

    /// <summary>Prints to the output (Luau <c>print</c>).</summary>
    /// <param name="args">Values to print.</param>
    public static void print(params object?[] args) { }

    /// <summary>Prints a warning to the output (Luau <c>warn</c>).</summary>
    /// <param name="args">Values to warn with.</param>
    public static void warn(params object?[] args) { }
}

/// <summary>
/// <c>SharedTable</c> operations (the Luau global <c>SharedTable.*</c>), as extension methods so the
/// call site reads naturally: <c>st.Increment("k", 1)</c> emits <c>SharedTable.increment(st, "k", 1)</c>.
/// </summary>
/// <remarks>
/// A <c>SharedTable</c> is the only value shared by reference across Actor VMs; its values must be
/// <c>Boolean</c>/<c>Number</c>/<c>Vector</c>/<c>String</c>/<c>SharedTable</c>.
/// </remarks>
public static class SharedTableOps
{
    /// <summary>Reads <paramref name="key"/> (<c>st[key]</c>).</summary>
    public static object Get(this SharedTable st, object key) => default!;

    /// <summary>Writes <paramref name="value"/> at <paramref name="key"/> (<c>st[key] = value</c>).</summary>
    public static void Set(this SharedTable st, object key, object value) { }

    /// <summary>Atomically adds <paramref name="delta"/> to <paramref name="key"/>; returns the new value.</summary>
    public static double Increment(this SharedTable st, object key, double delta) => default;

    /// <summary>Atomically replaces <paramref name="key"/> with <c>transform(current)</c>.</summary>
    public static void Update(this SharedTable st, object key, Func<object, object> transform) { }

    /// <summary>Number of entries.</summary>
    public static int Size(this SharedTable st) => default;

    /// <summary>Removes all entries.</summary>
    public static void Clear(this SharedTable st) { }
}
