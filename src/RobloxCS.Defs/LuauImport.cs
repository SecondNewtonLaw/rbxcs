namespace RobloxCS;

// Binds a C# declaration to a Luau module (a require path), so calls emit `require(path).Member(...)`
// instead of being transpiled from a body. Use for third-party Luau libraries: declare the surface
// in C# for type-checking, and the transpiler wires calls to the real Luau module.
//
//   [LuauImport("ReplicatedStorage.Packages.ZString")]
//   public static class ZString
//   {
//       public static string Format(string template, params object[] args) => default!;
//   }
//
// Module is a dotted DataModel path: first segment is a service, the rest are child instances.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class LuauImportAttribute(string module) : Attribute
{
    public string Module { get; } = module;
}

// Binds a C# declaration to a Wally package. Resolves to <PackagesMount>.<alias> (alias defaults to
// the C# type name = the Wally dependency alias in wally.toml). PackagesMount is set on the
// ProjectDescriptor (default ReplicatedStorage.Packages). Sugar over [LuauImport] for Wally packages.
//
//   [WallyPackage]                 // require(ReplicatedStorage.Packages.Signal)
//   public static class Signal { public static Signal @new() => default!; ... }
//
//   [WallyPackage("Promise")]      // alias differs from the C# type name
//   public static class MyPromise { ... }
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class WallyPackageAttribute(string alias = "") : Attribute
{
    public string Alias { get; } = alias;
}

// Binds a C# declaration to a hand-written .luau file that lives in the project. The path is the
// file's location relative to the project root, minus the .luau extension: the leading segment is
// the context folder (Shared/Server/Client/Packages) and is resolved through the mount config at
// build time, so renaming a mount doesn't strand the binding. Prefer this over [LuauImport] for
// your own .luau files; [LuauImport] stays for fixed external service paths.
//
//   [LuauModule("Shared/Handwritten")]   // -> require(<SharedMount>.Handwritten)
//   public static class Handwritten { public static string greet(string name) => default!; }
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class LuauModuleAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

// Overrides the Luau member name for a single method/property (default: the C# name).
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class LuauNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
