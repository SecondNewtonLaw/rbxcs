using System;
using Roblox;

namespace RobloxCS;

// Parallel Luau primitives. Desynchronize/Synchronize map to task.desynchronize / task.synchronize —
// legal only on a thread that belongs to an Actor VM (see [Actor]); off an Actor they raise. For is
// the compile-time fan-out: the transpiler lifts the body into a worker that runs across an Actor pool.
public static class ParallelLuau
{
    public static void Desynchronize() { }

    public static void Synchronize() { }

    public static void For(int fromInclusive, int toExclusive, Action<int> body) { }
}
