using RobloxCS;

namespace Demo;

// Binding to a third-party Luau library (e.g. a ZString package). No .NET body — declares the
// surface for type-checking; calls emit require("...").Member(...).
[LuauImport("ReplicatedStorage.Packages.ZString")]
public static class ZString
{
    public static string Format(string template, params object[] args) => default!;

    [LuauName("concat")]
    public static string Join(params string[] parts) => default!;
}

// Binding to the hand-written Shared/Handwritten.luau. Path is folder-relative; the Shared segment
// resolves through the mount config, so renaming SharedMount won't strand this.
[LuauModule("Shared/Handwritten")]
public static class Handwritten
{
    public static string greet(string name) => default!;
}

[Shared]
public class ExternTest
{
    public string Formatted()
    {
        return ZString.Format("hi {0}", "world");
    }

    public string Greeting()
    {
        return Handwritten.greet("world");
    }

    public string Joined()
    {
        return ZString.Join("a", "b", "c");
    }

    // Roblox DateTime datatype -> native DateTime.now(), no warning.
    public long UnixNow()
    {
        var dt = Roblox.DateTime.now();
        return dt.UnixTimestamp;
    }
}
