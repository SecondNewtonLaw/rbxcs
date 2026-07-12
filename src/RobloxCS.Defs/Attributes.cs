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

// Parallel Luau. An [Actor] entry script emits its runner Script beneath a Roblox `Actor` instance,
// so its thread (and everything it requires) runs in that Actor's isolated Luau VM — the only place
// `task.desynchronize` is legal. Roblox actors are message-passing (SharedTable / SendMessage), NOT
// shared-memory threads: there is no faithful `System.Threading.Thread`, so use this model instead.
[AttributeUsage(AttributeTargets.Class)]
public sealed class ActorAttribute : Attribute;

// Marks a method whose whole body runs desynchronized (parallel phase). The transpiler wraps it in
// RBXCS.parallel, which desyncs only when the current thread belongs to an Actor VM and otherwise
// runs serial — so it is safe to call from anywhere. Body must not mutate the DataModel.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ParallelAttribute : Attribute;
