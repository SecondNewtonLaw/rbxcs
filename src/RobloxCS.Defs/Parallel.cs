using System;
using Roblox;

namespace RobloxCS;

/// <summary>
/// Parallel Luau primitives. Maps to the Luau <c>task</c> library's parallel-phase controls, plus a
/// compile-time <see cref="For"/> fan-out.
/// </summary>
/// <remarks>
/// Named <c>ParallelLuau</c> to avoid colliding with <c>System.Threading.Tasks.Parallel</c>.
/// <see cref="Desynchronize"/>/<see cref="Synchronize"/> are legal only on a thread that belongs to an
/// Actor VM (see <see cref="ActorAttribute"/>); off an Actor they raise.
/// </remarks>
public static class ParallelLuau
{
    /// <summary>
    /// Enters the parallel phase (<c>task.desynchronize</c>). Legal only on an Actor-VM thread; raises
    /// otherwise.
    /// </summary>
    public static void Desynchronize() { }

    /// <summary>Returns to the serial phase (<c>task.synchronize</c>). A no-op if already serial.</summary>
    public static void Synchronize() { }

    /// <summary>
    /// Runs <paramref name="body"/> for each index in <c>[fromInclusive, toExclusive)</c>, fanned out
    /// across an Actor pool when the body is self-contained.
    /// </summary>
    /// <remarks>
    /// The transpiler lifts the body into a worker that runs across a pool of cloned Actors when it
    /// captures only <c>SharedTable</c>s (which cross a VM by reference) and pulls in no other module;
    /// otherwise it falls back to a correct sequential loop with a diagnostic. A runtime closure cannot
    /// cross an Actor VM, so real parallelism is compiled, not passed.
    /// </remarks>
    /// <param name="fromInclusive">First index (inclusive).</param>
    /// <param name="toExclusive">One past the last index (exclusive).</param>
    /// <param name="body">Loop body; receives the index. Capture only <see cref="SharedTable"/>s to parallelize.</param>
    public static void For(int fromInclusive, int toExclusive, Action<int> body) { }
}
