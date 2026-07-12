namespace RobloxCS;

/// <summary>
/// Places a type's transpiled module under <c>ServerScriptService</c> (the <c>[Server]</c> mount).
/// One context attribute per top-level type; the transpiler routes the emitted module accordingly.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ServerAttribute : Attribute;

/// <summary>Places a type's transpiled module under the client mount (<c>StarterPlayerScripts</c>).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ClientAttribute : Attribute;

/// <summary>Places a type's transpiled module under the shared mount (<c>ReplicatedStorage</c>).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class SharedAttribute : Attribute;

/// <summary>
/// Marks an entry-point type: it emits an executable <c>Script</c> whose body runs the type's
/// <c>static void Main()</c>. Unmarked types emit passive <c>ModuleScript</c>s.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ScriptAttribute : Attribute;

/// <summary>
/// Like <see cref="ScriptAttribute"/>, but emits a <c>LocalScript</c> (client-side executable).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LocalScriptAttribute : Attribute;

/// <summary>
/// Runs an entry <see cref="ScriptAttribute"/>/<see cref="LocalScriptAttribute"/> class beneath a
/// Roblox <c>Actor</c> instance, so its thread runs in that Actor's isolated Luau VM — the only place
/// <c>task.desynchronize</c> is legal.
/// </summary>
/// <remarks>
/// Roblox actors are message-passing, isolated VMs, not shared-memory threads: there is no faithful
/// <c>System.Threading.Thread</c>, so use this model with <see cref="ParallelLuau"/> instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ActorAttribute : Attribute;

/// <summary>
/// Marks a method whose whole body runs desynchronized (the parallel phase). The transpiler wraps it
/// in the runtime's parallel helper, which desyncs only when the current thread belongs to an Actor VM
/// and otherwise runs serial — so it is safe to call from anywhere.
/// </summary>
/// <remarks>
/// The body must not mutate the DataModel: the transpiler checks it against the API dump's
/// <c>ThreadSafety</c> tags and flags any <c>Unsafe</c> member or write to a read-only member.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ParallelAttribute : Attribute;
