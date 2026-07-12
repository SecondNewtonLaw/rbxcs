namespace RobloxCS;

/// <summary>
/// Binds a C# declaration to a Luau module (a <c>require</c> path), so calls emit
/// <c>require(path).Member(...)</c> instead of being transpiled from a body. Use it for third-party
/// Luau libraries: declare the surface in C# for type-checking, and the transpiler wires calls to the
/// real Luau module.
/// </summary>
/// <remarks>
/// The module is a dotted DataModel path: the first segment is a service, the rest are child instances.
/// Prefer <see cref="LuauModuleAttribute"/> for your own <c>.luau</c> files; use this for fixed
/// external service paths.
/// </remarks>
/// <example>
/// <code>
/// [LuauImport("ReplicatedStorage.Packages.ZString")]
/// public static class ZString
/// {
///     public static string Format(string template, params object[] args) => default!;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class LuauImportAttribute(string module) : Attribute
{
    /// <summary>The dotted DataModel path of the Luau module (e.g. <c>ReplicatedStorage.Packages.X</c>).</summary>
    public string Module { get; } = module;
}

/// <summary>
/// Binds a C# declaration to a Wally package. Resolves to <c>&lt;PackagesMount&gt;.&lt;alias&gt;</c> —
/// sugar over <see cref="LuauImportAttribute"/> for Wally packages.
/// </summary>
/// <remarks>
/// The alias defaults to the C# type name (which should match the Wally dependency alias in
/// <c>wally.toml</c>). <c>PackagesMount</c> is configured on <see cref="ProjectDescriptor"/> (default
/// <c>ReplicatedStorage.Packages</c>).
/// </remarks>
/// <example>
/// <code>
/// [WallyPackage]                 // require(ReplicatedStorage.Packages.Signal)
/// public static class Signal { public static Signal @new() => default!; }
///
/// [WallyPackage("Promise")]      // alias differs from the C# type name
/// public static class MyPromise { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class WallyPackageAttribute(string alias = "") : Attribute
{
    /// <summary>The Wally dependency alias; empty means "use the C# type name".</summary>
    public string Alias { get; } = alias;
}

/// <summary>
/// Binds a C# declaration to a hand-written <c>.luau</c> file that lives in the project.
/// </summary>
/// <remarks>
/// The path is the file's location relative to the project root, minus the <c>.luau</c> extension. The
/// leading segment is the context folder (<c>Shared</c>/<c>Server</c>/<c>Client</c>/<c>Packages</c>)
/// and is resolved through the mount config at build time, so renaming a mount doesn't strand the
/// binding. Prefer this over <see cref="LuauImportAttribute"/> for your own <c>.luau</c> files.
/// </remarks>
/// <example>
/// <code>
/// [LuauModule("Shared/Handwritten")]   // -> require(&lt;SharedMount&gt;.Handwritten)
/// public static class Handwritten { public static string greet(string name) => default!; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class LuauModuleAttribute(string path) : Attribute
{
    /// <summary>Project-relative path to the <c>.luau</c> file, without the extension.</summary>
    public string Path { get; } = path;
}

/// <summary>
/// Overrides the emitted Luau member name for a single method or property (default: the C# name).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
public sealed class LuauNameAttribute(string name) : Attribute
{
    /// <summary>The Luau member name to emit at call sites.</summary>
    public string Name { get; } = name;
}
