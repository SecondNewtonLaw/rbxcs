namespace Roblox;

// Luau globals (not Roblox instance classes, so not in the API dump). Instance / DataModel /
// Workspace / all services + enums + datatypes come from RobloxApi.g.cs (generated from the dump).
public static class Globals
{
    public static readonly DataModel game = default!;
    public static readonly Workspace workspace = default!;
    public static readonly Instance script = default!; // the running script instance (script:GetActor(), etc.)
    public static void print(params object?[] args) { }
    public static void warn(params object?[] args) { }
}

// Luau `SharedTable` ops (Roblox global SharedTable.*), as extension methods so the C# call site
// reads naturally: st.Increment("k", 1) -> SharedTable.increment(st, "k", 1). SharedTable is the only
// value shared by reference across Actor VMs; values must be Boolean/Number/Vector/String/SharedTable.
public static class SharedTableOps
{
    public static object Get(this SharedTable st, object key) => default!;
    public static void Set(this SharedTable st, object key, object value) { }
    public static double Increment(this SharedTable st, object key, double delta) => default;
    public static void Update(this SharedTable st, object key, Func<object, object> transform) { }
    public static int Size(this SharedTable st) => default;
    public static void Clear(this SharedTable st) { }
}
