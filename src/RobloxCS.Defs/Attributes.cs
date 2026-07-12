namespace RobloxCS;

// Runtime-context placement. One per top-level type; the transpiler routes the emitted
// module to the matching Roblox service mount (decision #10).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ServerAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ClientAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class SharedAttribute : Attribute;

// Entry-point kind. A type marked entry emits an executable Script/LocalScript whose body is
// its `static void Main()`. Unmarked types emit passive ModuleScripts.
[AttributeUsage(AttributeTargets.Class)]
public sealed class ScriptAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class LocalScriptAttribute : Attribute;
